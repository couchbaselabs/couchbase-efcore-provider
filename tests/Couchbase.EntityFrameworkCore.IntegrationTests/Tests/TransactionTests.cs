using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCode.IntegrationTests.Models;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Couchbase.KeyValue;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

[Collection(CouchbaseTestingCollection.Name)]
public class TransactionTests(
    BloggingFixture bloggingFixture,
    ITestOutputHelper outputHelper)
{
    [Fact]
    public async Task BeginTransaction_SaveChanges_Commit_PersistsData()
    {
        await using var context = bloggingFixture.GetDbContext();
        var blog = new BloggingFixture.Blog
        {
            BlogId = 9001,
            Url = "http://transaction-test.com",
            Rating = 5
        };

        try
        {
            // Use DurabilityLevel.None for single-node test cluster
            await using var transaction = await context.Database.BeginCouchbaseTransactionAsync(DurabilityLevel.None);

            context.Blogs.Add(blog);
            await context.SaveChangesAsync();

            await transaction.CommitAsync();

            // Poll for consistency with bounded timeout
            var savedBlog = await PollingHelper.PollForResultAsync(
                async () =>
                {
                    await using var verifyContext = bloggingFixture.GetDbContext();
                    return await verifyContext.Blogs.FindAsync(blog.BlogId);
                },
                result => result != null,
                TimeSpan.FromSeconds(5));

            Assert.NotNull(savedBlog);
            Assert.Equal("http://transaction-test.com", savedBlog.Url);
        }
        finally
        {
            context.Remove(blog);
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task BeginTransaction_SaveChanges_Rollback_DoesNotPersistData()
    {
        await using var context = bloggingFixture.GetDbContext();
        var blog = new BloggingFixture.Blog
        {
            BlogId = 9002,
            Url = "http://rollback-test.com",
            Rating = 3
        };

        try
        {
            await using var transaction = await context.Database.BeginCouchbaseTransactionAsync(DurabilityLevel.None);

            context.Blogs.Add(blog);
            await context.SaveChangesAsync();

            await transaction.RollbackAsync();

            // A single immediate read (or polling until it returns null) could still be fooled by
            // a momentarily-stale KV read that returns null even though the document actually
            // persisted — poll briefly for it to ever appear, then assert it never did, mirroring
            // the cross-bucket rollback/leak checks in CrossBucketTransactionTests.
            await using var verifyContext = bloggingFixture.GetDbContext();
            var leakedBlog = await PollingHelper.PollForResultAsync(
                () => verifyContext.Blogs.FindAsync(blog.BlogId).AsTask(),
                result => result != null,
                TimeSpan.FromSeconds(2));
            Assert.Null(leakedBlog);
        }
        finally
        {
            await using var cleanupContext = bloggingFixture.GetDbContext();
            var persistedBlog = await cleanupContext.Blogs.FindAsync(blog.BlogId);
            if (persistedBlog != null)
            {
                cleanupContext.Remove(persistedBlog);
                await cleanupContext.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task BeginTransaction_MultipleEntities_Commit_PersistsAll()
    {
        await using var context = bloggingFixture.GetDbContext();
        var blog1 = new BloggingFixture.Blog { BlogId = 9003, Url = "http://multi1.com", Rating = 4 };
        var blog2 = new BloggingFixture.Blog { BlogId = 9004, Url = "http://multi2.com", Rating = 4 };

        try
        {
            await using var transaction = await context.Database.BeginCouchbaseTransactionAsync(DurabilityLevel.None);

            context.Blogs.AddRange(blog1, blog2);
            await context.SaveChangesAsync();

            await transaction.CommitAsync();

            // Poll for both blogs to be persisted
            var (saved1, saved2) = await PollingHelper.PollForResultAsync(
                async () =>
                {
                    await using var verifyContext = bloggingFixture.GetDbContext();
                    var s1 = await verifyContext.Blogs.FindAsync(blog1.BlogId);
                    var s2 = await verifyContext.Blogs.FindAsync(blog2.BlogId);
                    return (s1, s2);
                },
                result => result.s1 != null && result.s2 != null,
                TimeSpan.FromSeconds(5));

            Assert.NotNull(saved1);
            Assert.NotNull(saved2);
        }
        finally
        {
            context.RemoveRange(blog1, blog2);
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task BeginTransaction_Update_Commit_UpdatesDocument()
    {
        await using var context = bloggingFixture.GetDbContext();
        var blog = new BloggingFixture.Blog
        {
            BlogId = 9005,
            Url = "http://update-original.com",
            Rating = 2
        };

        // First create the blog without transaction
        context.Blogs.Add(blog);
        await context.SaveChangesAsync();

        try
        {
            // Poll until blog is persisted
            await PollingHelper.PollUntilAsync(async () =>
            {
                await using var checkContext = bloggingFixture.GetDbContext();
                return await checkContext.Blogs.FindAsync(blog.BlogId) != null;
            }, TimeSpan.FromSeconds(5));

            // Now update in transaction
            await using var transaction = await context.Database.BeginCouchbaseTransactionAsync(DurabilityLevel.None);

            var existingBlog = await context.Blogs.FindAsync(blog.BlogId);
            existingBlog!.Url = "http://update-modified.com";
            existingBlog.Rating = 5;
            context.Blogs.Update(existingBlog);
            await context.SaveChangesAsync();

            await transaction.CommitAsync();

            // Poll for updated values
            var updatedBlog = await PollingHelper.PollForResultAsync(
                async () =>
                {
                    await using var verifyContext = bloggingFixture.GetDbContext();
                    return await verifyContext.Blogs.FindAsync(blog.BlogId);
                },
                result => result?.Url == "http://update-modified.com",
                TimeSpan.FromSeconds(5));

            Assert.NotNull(updatedBlog);
            Assert.Equal("http://update-modified.com", updatedBlog.Url);
            Assert.Equal(5, updatedBlog.Rating);
        }
        finally
        {
            context.Remove(blog);
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task BeginTransaction_Delete_Commit_RemovesDocument()
    {
        await using var context = bloggingFixture.GetDbContext();
        var blog = new BloggingFixture.Blog
        {
            BlogId = 9006,
            Url = "http://delete-test.com",
            Rating = 1
        };

        // First create the blog without transaction
        context.Blogs.Add(blog);
        await context.SaveChangesAsync();

        try
        {
            // Poll until blog is persisted
            await PollingHelper.PollUntilAsync(async () =>
            {
                await using var checkContext = bloggingFixture.GetDbContext();
                return await checkContext.Blogs.FindAsync(blog.BlogId) != null;
            }, TimeSpan.FromSeconds(5));

            // Delete in transaction
            await using var transaction = await context.Database.BeginCouchbaseTransactionAsync(DurabilityLevel.None);

            var existingBlog = await context.Blogs.FindAsync(blog.BlogId);
            context.Blogs.Remove(existingBlog!);
            await context.SaveChangesAsync();

            await transaction.CommitAsync();

            // Poll until deletion is confirmed
            await PollingHelper.PollUntilAsync(async () =>
            {
                await using var verifyContext = bloggingFixture.GetDbContext();
                return await verifyContext.Blogs.FindAsync(blog.BlogId) == null;
            }, TimeSpan.FromSeconds(5));

            // Final verification
            await using var finalContext = bloggingFixture.GetDbContext();
            var deletedBlog = await finalContext.Blogs.FindAsync(blog.BlogId);
            Assert.Null(deletedBlog);
        }
        finally
        {
            // If any step above failed before deletion was confirmed (or deletion itself
            // regressed), the fixed BlogId (9006) could remain in the collection and make later
            // integration test runs flaky — remove it if it's still there.
            await using var cleanupContext = bloggingFixture.GetDbContext();
            var persistedBlog = await cleanupContext.Blogs.FindAsync(blog.BlogId);
            if (persistedBlog != null)
            {
                cleanupContext.Blogs.Remove(persistedBlog);
                await cleanupContext.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task Transaction_Dispose_WithoutCommit_DoesNotPersist()
    {
        await using var context = bloggingFixture.GetDbContext();
        var blog = new BloggingFixture.Blog
        {
            BlogId = 9007,
            Url = "http://dispose-test.com"
        };

        try
        {
            {
                await using var transaction = await context.Database.BeginCouchbaseTransactionAsync(DurabilityLevel.None);

                context.Blogs.Add(blog);
                await context.SaveChangesAsync();

                // Transaction disposed without commit
            }

            // Polling until FindAsync returns null could false-pass on a momentarily-stale KV
            // read even if the document actually persisted — poll briefly for it to ever appear,
            // then assert it never did, mirroring the rollback/leak checks elsewhere in this file.
            await using var verifyContext = bloggingFixture.GetDbContext();
            var leakedBlog = await PollingHelper.PollForResultAsync(
                () => verifyContext.Blogs.FindAsync(blog.BlogId).AsTask(),
                result => result != null,
                TimeSpan.FromSeconds(2));
            Assert.Null(leakedBlog);
        }
        finally
        {
            await using var cleanupContext = bloggingFixture.GetDbContext();
            var persistedBlog = await cleanupContext.Blogs.FindAsync(blog.BlogId);
            if (persistedBlog != null)
            {
                cleanupContext.Blogs.Remove(persistedBlog);
                await cleanupContext.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task UseTransaction_WithValidCouchbaseTransaction_Works()
    {
        await using var context = bloggingFixture.GetDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        var dbTransaction = (CouchbaseDbTransaction)connection.BeginTransaction();

        // Use the transaction with the context
        await context.Database.UseTransactionAsync(dbTransaction);

        var currentTransaction = context.Database.CurrentTransaction;
        Assert.NotNull(currentTransaction);

        dbTransaction.Rollback();
        dbTransaction.Dispose();
    }

    [Fact]
    public async Task Transaction_GetCurrentTransaction_ReturnsActiveTransaction()
    {
        await using var context = bloggingFixture.GetDbContext();

        Assert.Null(context.Database.CurrentTransaction);

        await using var transaction = await context.Database.BeginCouchbaseTransactionAsync(DurabilityLevel.None);

        Assert.NotNull(context.Database.CurrentTransaction);
    }

    [Fact]
    public async Task SaveChangesAsync_WithoutTransaction_PersistsImmediately()
    {
        await using var context = bloggingFixture.GetDbContext();
        var blog = new BloggingFixture.Blog
        {
            BlogId = 9008,
            Url = "http://no-transaction.com",
            Rating = 4
        };

        try
        {
            // No transaction - should persist immediately
            context.Blogs.Add(blog);
            await context.SaveChangesAsync();

            // Poll for persistence
            var savedBlog = await PollingHelper.PollForResultAsync(
                async () =>
                {
                    await using var verifyContext = bloggingFixture.GetDbContext();
                    return await verifyContext.Blogs.FindAsync(blog.BlogId);
                },
                result => result != null,
                TimeSpan.FromSeconds(5));

            Assert.NotNull(savedBlog);
        }
        finally
        {
            context.Remove(blog);
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task BeginTransaction_TwoSaveChanges_Commit_PersistsBoth()
    {
        // Each SaveChanges must queue only its own new work; re-queuing the first save's insert
        // would fail the commit with DocumentExistsException.
        await using var context = bloggingFixture.GetDbContext();
        var first = new BloggingFixture.Blog { BlogId = 9110, Url = "http://two-saves-1.com", Rating = 4 };
        var second = new BloggingFixture.Blog { BlogId = 9111, Url = "http://two-saves-2.com", Rating = 4 };

        try
        {
            await using var transaction = await context.Database.BeginCouchbaseTransactionAsync(DurabilityLevel.None);

            context.Blogs.Add(first);
            await context.SaveChangesAsync();
            context.Blogs.Add(second);
            await context.SaveChangesAsync();

            await transaction.CommitAsync();

            Assert.NotNull(await PollForBlogAsync(first.BlogId));
            Assert.NotNull(await PollForBlogAsync(second.BlogId));
        }
        finally
        {
            await RemoveBlogsAsync(first.BlogId, second.BlogId);
        }
    }

    [Fact]
    public async Task BeginTransaction_Rollback_RestoresSavedEntitiesToPending()
    {
        // Retry-on-the-same-context relies on entities going back to their pending state when the
        // transaction's work is not persisted.
        await using var context = bloggingFixture.GetDbContext();
        var blog = new BloggingFixture.Blog { BlogId = 9112, Url = "http://rollback-state.com", Rating = 3 };

        try
        {
            await using var transaction = await context.Database.BeginCouchbaseTransactionAsync(DurabilityLevel.None);

            context.Blogs.Add(blog);
            await context.SaveChangesAsync();
            Assert.Equal(EntityState.Unchanged, context.Entry(blog).State);

            await transaction.RollbackAsync();

            Assert.Equal(EntityState.Added, context.Entry(blog).State);
        }
        finally
        {
            await RemoveBlogsAsync(blog.BlogId);
        }
    }

    [Fact]
    public async Task Savepoint_RollbackTo_Commit_PersistsOnlyWorkBeforeTheSavepoint()
    {
        await using var context = bloggingFixture.GetDbContext();
        var kept = new BloggingFixture.Blog { BlogId = 9101, Url = "http://savepoint-kept.com", Rating = 5 };
        var discarded = new BloggingFixture.Blog { BlogId = 9102, Url = "http://savepoint-discarded.com", Rating = 1 };

        try
        {
            await using var transaction = await context.Database.BeginCouchbaseTransactionAsync(DurabilityLevel.None);

            context.Blogs.Add(kept);
            await context.SaveChangesAsync();

            await transaction.CreateSavepointAsync("before_discarded");

            context.Blogs.Add(discarded);
            await context.SaveChangesAsync();

            await transaction.RollbackToSavepointAsync("before_discarded");
            await transaction.CommitAsync();

            Assert.NotNull(await PollForBlogAsync(kept.BlogId));
            await AssertNeverAppearsAsync(discarded.BlogId);
        }
        finally
        {
            await RemoveBlogsAsync(kept.BlogId, discarded.BlogId);
        }
    }

    [Fact]
    public async Task Savepoint_RollbackTo_ThenContinue_DoesNotRequeueEarlierWork()
    {
        await using var context = bloggingFixture.GetDbContext();
        var kept = new BloggingFixture.Blog { BlogId = 9105, Url = "http://savepoint-continue-kept.com", Rating = 5 };
        var rolledBack = new BloggingFixture.Blog { BlogId = 9106, Url = "http://savepoint-continue-rolledback.com", Rating = 1 };
        var after = new BloggingFixture.Blog { BlogId = 9107, Url = "http://savepoint-continue-after.com", Rating = 4 };

        try
        {
            await using var transaction = await context.Database.BeginCouchbaseTransactionAsync(DurabilityLevel.None);

            context.Blogs.Add(kept);
            await context.SaveChangesAsync();
            await transaction.CreateSavepointAsync("sp");
            context.Blogs.Add(rolledBack);
            await context.SaveChangesAsync();

            await transaction.RollbackToSavepointAsync("sp");

            // Work saved before the savepoint stays saved; work after it is pending again, exactly
            // as after a failed SaveChanges.
            Assert.Equal(EntityState.Unchanged, context.Entry(kept).State);
            Assert.Equal(EntityState.Added, context.Entry(rolledBack).State);

            // Discard the rolled-back work and carry on in the same transaction. The next save must
            // not re-queue `kept` (a duplicate insert would fail the commit).
            context.Entry(rolledBack).State = EntityState.Detached;
            context.Blogs.Add(after);
            await context.SaveChangesAsync();

            await transaction.CommitAsync();

            Assert.NotNull(await PollForBlogAsync(kept.BlogId));
            Assert.NotNull(await PollForBlogAsync(after.BlogId));
            await AssertNeverAppearsAsync(rolledBack.BlogId);
        }
        finally
        {
            await RemoveBlogsAsync(kept.BlogId, rolledBack.BlogId, after.BlogId);
        }
    }

    [Fact]
    public async Task Savepoint_RollbackTo_Commit_PersistsUpdateAsOfTheSavepoint()
    {
        var blog = new BloggingFixture.Blog { BlogId = 9108, Url = "http://savepoint-update.com", Rating = 1 };
        await using (var seedContext = bloggingFixture.GetDbContext())
        {
            seedContext.Blogs.Add(blog);
            await seedContext.SaveChangesAsync();
        }

        try
        {
            await using var context = bloggingFixture.GetDbContext();
            var tracked = await PollForBlogAsync(blog.BlogId, context);
            Assert.NotNull(tracked);

            await using var transaction = await context.Database.BeginCouchbaseTransactionAsync(DurabilityLevel.None);

            tracked!.Rating = 2;
            await context.SaveChangesAsync();
            await transaction.CreateSavepointAsync("after_first_update");
            tracked.Rating = 3;
            await context.SaveChangesAsync();

            await transaction.RollbackToSavepointAsync("after_first_update");

            // The second update was rolled back, so it is pending again against the value as of
            // the savepoint.
            Assert.Equal(EntityState.Modified, context.Entry(tracked).State);
            Assert.Equal(2, context.Entry(tracked).Property(b => b.Rating).OriginalValue);

            await transaction.CommitAsync();

            await using var verifyContext = bloggingFixture.GetDbContext();
            var persisted = await PollingHelper.PollForResultAsync(
                () => verifyContext.Blogs.AsNoTracking().FirstOrDefaultAsync(b => b.BlogId == blog.BlogId),
                result => result is { Rating: 2 },
                TimeSpan.FromSeconds(5));
            Assert.NotNull(persisted);
            Assert.Equal(2, persisted!.Rating);
        }
        finally
        {
            await RemoveBlogsAsync(blog.BlogId);
        }
    }

    [Fact]
    public async Task Savepoint_Release_Commit_PersistsAllWork()
    {
        await using var context = bloggingFixture.GetDbContext();
        var first = new BloggingFixture.Blog { BlogId = 9103, Url = "http://savepoint-release-1.com", Rating = 4 };
        var second = new BloggingFixture.Blog { BlogId = 9104, Url = "http://savepoint-release-2.com", Rating = 4 };

        try
        {
            await using var transaction = await context.Database.BeginCouchbaseTransactionAsync(DurabilityLevel.None);

            context.Blogs.Add(first);
            await context.SaveChangesAsync();
            await transaction.CreateSavepointAsync("sp");
            context.Blogs.Add(second);
            await context.SaveChangesAsync();
            await transaction.ReleaseSavepointAsync("sp");
            await transaction.CommitAsync();

            Assert.NotNull(await PollForBlogAsync(first.BlogId));
            Assert.NotNull(await PollForBlogAsync(second.BlogId));
        }
        finally
        {
            await RemoveBlogsAsync(first.BlogId, second.BlogId);
        }
    }

    private Task<BloggingFixture.Blog?> PollForBlogAsync(int blogId, BloggingDbContext? context = null)
        => PollingHelper.PollForResultAsync(
            async () =>
            {
                if (context != null)
                {
                    return await context.Blogs.FindAsync(blogId);
                }

                await using var verifyContext = bloggingFixture.GetDbContext();
                return await verifyContext.Blogs.FindAsync(blogId);
            },
            result => result != null,
            TimeSpan.FromSeconds(5));

    // Poll briefly for the document to ever appear, then assert it never did — a single immediate
    // read could be fooled by a momentarily stale KV read.
    private async Task AssertNeverAppearsAsync(int blogId)
    {
        await using var leakContext = bloggingFixture.GetDbContext();
        var leaked = await PollingHelper.PollForResultAsync(
            () => leakContext.Blogs.FindAsync(blogId).AsTask(),
            result => result != null,
            TimeSpan.FromSeconds(2));
        Assert.Null(leaked);
    }

    private async Task RemoveBlogsAsync(params int[] blogIds)
    {
        await using var cleanupContext = bloggingFixture.GetDbContext();
        foreach (var id in blogIds)
        {
            var persisted = await cleanupContext.Blogs.FindAsync(id);
            if (persisted != null)
            {
                cleanupContext.Remove(persisted);
            }
        }

        await cleanupContext.SaveChangesAsync();
    }
}
