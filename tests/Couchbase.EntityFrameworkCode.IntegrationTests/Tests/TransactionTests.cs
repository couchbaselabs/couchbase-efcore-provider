using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore.Storage.Internal;
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
            await using var transaction = await context.Database.BeginTransactionAsync();

            context.Blogs.Add(blog);
            await context.SaveChangesAsync();

            await transaction.CommitAsync();

            // RYOW delay
            await Task.Delay(100);

            // Verify persistence
            await using var verifyContext = bloggingFixture.GetDbContext();
            var savedBlog = await verifyContext.Blogs.FindAsync(blog.BlogId);
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
            await using var transaction = await context.Database.BeginTransactionAsync();

            context.Blogs.Add(blog);
            await context.SaveChangesAsync();

            await transaction.RollbackAsync();

            // Verify document was not persisted
            await Task.Delay(100);
            await using var verifyContext = bloggingFixture.GetDbContext();
            var notSavedBlog = await verifyContext.Blogs.FindAsync(blog.BlogId);
            Assert.Null(notSavedBlog);
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
            await using var transaction = await context.Database.BeginTransactionAsync();

            context.Blogs.AddRange(blog1, blog2);
            await context.SaveChangesAsync();

            await transaction.CommitAsync();

            await Task.Delay(100);

            await using var verifyContext = bloggingFixture.GetDbContext();
            var saved1 = await verifyContext.Blogs.FindAsync(blog1.BlogId);
            var saved2 = await verifyContext.Blogs.FindAsync(blog2.BlogId);

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
            await Task.Delay(100);

            // Now update in transaction
            await using var transaction = await context.Database.BeginTransactionAsync();

            var existingBlog = await context.Blogs.FindAsync(blog.BlogId);
            existingBlog!.Url = "http://update-modified.com";
            existingBlog.Rating = 5;
            context.Blogs.Update(existingBlog);
            await context.SaveChangesAsync();

            await transaction.CommitAsync();

            await Task.Delay(100);

            // Verify update
            await using var verifyContext = bloggingFixture.GetDbContext();
            var updatedBlog = await verifyContext.Blogs.FindAsync(blog.BlogId);
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

        await Task.Delay(100);

        // Delete in transaction
        await using var transaction = await context.Database.BeginTransactionAsync();

        var existingBlog = await context.Blogs.FindAsync(blog.BlogId);
        context.Blogs.Remove(existingBlog!);
        await context.SaveChangesAsync();

        await transaction.CommitAsync();

        await Task.Delay(100);

        // Verify deletion
        await using var verifyContext = bloggingFixture.GetDbContext();
        var deletedBlog = await verifyContext.Blogs.FindAsync(blog.BlogId);
        Assert.Null(deletedBlog);
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
                await using var transaction = await context.Database.BeginTransactionAsync();

                context.Blogs.Add(blog);
                await context.SaveChangesAsync();

                // Transaction disposed without commit
            }

            await Task.Delay(100);

            // Verify not persisted
            await using var verifyContext = bloggingFixture.GetDbContext();
            var notSaved = await verifyContext.Blogs.FindAsync(blog.BlogId);
            Assert.Null(notSaved);
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

        await using var transaction = await context.Database.BeginTransactionAsync();

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

            await Task.Delay(100);

            // Verify persistence
            await using var verifyContext = bloggingFixture.GetDbContext();
            var savedBlog = await verifyContext.Blogs.FindAsync(blog.BlogId);
            Assert.NotNull(savedBlog);
        }
        finally
        {
            context.Remove(blog);
            await context.SaveChangesAsync();
        }
    }
}
