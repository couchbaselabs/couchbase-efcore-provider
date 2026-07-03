using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

[Collection(CouchbaseTestingCollection.Name)]
public class CrudTests(
    TravelSampleFixture travelSampleFixture,
    BloggingFixture bloggingFixture,
    ITestOutputHelper outputHelper)
{
    [Fact]
    public async Task Test_ExecuteUpdate()
    {
        await using var context = travelSampleFixture.GetDbContext();
        var airline1 = new TravelSampleFixture.Airline
        {
            Type = "airline",
            Id = 666,
            Callsign = "MILE-AIR",
            Country = "United States",
            Icao = "MLA",
            Iata = "Q5",
            Name = "40-Mile Air"
        };

        var airline2 = new TravelSampleFixture.Airline
        {
            Type = "airline",
            Id = 667,
            Callsign = "MILE-AIR",
            Country = "United States",
            Icao = "MLA",
            Iata = "Q5",
            Name = "40-Mile Air"
        };

        try
        {
            context.Update(airline1);
            context.Update(airline2);

            var inserted = await context.SaveChangesAsync();
            Assert.Equal(2, inserted);

            //RYOW is currently not supported, so we need a brief delay for indexing etc.
            await Task.Delay(100);

            var count = await context.Airlines.CountAsync(a => a.Id > 665 && a.Id < 668);
            Assert.Equal(2, count);

            await context.Airlines
                .Where(a => a.Id > 665 && a.Id < 668)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(a => a.Country, "Banana Republic"));

            var count2 = await context.Airlines.CountAsync(a => a.Country == "Banana Republic");
            Assert.Equal(2, count2);
        }
        finally
        {
            context.Remove(airline1);
            context.Remove(airline2);
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Test_ExecuteDelete()
    {
        await using var context = travelSampleFixture.GetDbContext();
        var airline1 = new TravelSampleFixture.Airline
        {
            Type = "airline",
            Id = 777,
            Callsign = "MILE-AIR",
            Country = "United States",
            Icao = "MLA",
            Iata = "Q5",
            Name = "40-Mile Air"
        };

        var airline2 = new TravelSampleFixture.Airline
        {
            Type = "airline",
            Id = 778,
            Callsign = "MILE-AIR",
            Country = "United States",
            Icao = "MLA",
            Iata = "Q5",
            Name = "40-Mile Air"
        };

        try
        {
            context.Update(airline1);
            context.Update(airline2);

            await context.SaveChangesAsync();
            await Task.Delay(100);
            var count = await context.Airlines.CountAsync(a => a.Id > 776 && a.Id < 779);
            Assert.Equal(2, count);

            await context.Airlines
                .Where(a => a.Id > 776 && a.Id < 779)
                .ExecuteDeleteAsync();

            var count2 = await context.Airlines.CountAsync(a => a.Id > 776 && a.Id < 779);
            Assert.Equal(0, count2);
        }
        finally
        {
            if (context.Remove(airline1).State != EntityState.Deleted)
            {
                await context.SaveChangesAsync();
            }

            if (context.Remove(airline2).State != EntityState.Deleted)
            {
                await context.SaveChangesAsync();
            }
        }
    }

    [Fact(Skip = "Needs a writable 'user' collection in travel-sample AND User.Addresses mapped as OwnsMany (currently .Ignore()'d in TravelSampleDbContext). Owned types are supported; enabling this is model/fixture work.")]
    public async Task Test_ComplexObject()
    {
        await using var context = travelSampleFixture.GetDbContext();
        var user = new TravelSampleFixture.User
        {
            Name = "Jeff Morris",
            DrivingLicence = "A^&*GUIOO",
            PreferredEmail = "jefry@job.com",
            Addresses =
            [
                new TravelSampleFixture.Address
                {
                    City = "Huntington Beach", Country = "USA", HomeAddress = "10032 Stonybrook Drive", ID = "1",
                    Type = "Home"
                },
                new TravelSampleFixture.Address
                {
                    City = "Newport", ID = "2", HomeAddress = "123 Balboa Ave", Country = "USA", Type = "Work"
                }
            ]
        };
        try
        {
            context.Update(user);
            await context.SaveChangesAsync();
        }
        finally
        {
            context.Remove(user);
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Test_AddAsync()
    {
        await using var context = travelSampleFixture.GetDbContext();
        var airline = new TravelSampleFixture.Airline
        {
            Type = "airline",
            Id = 11,
            Callsign = "MILE-AIR",
            Country = "United States",
            Icao = "MLA",
            Iata = "Q5",
            Name = "40-Mile Air"
        };
        try
        {
            var saved = context.Update(airline);
            await context.SaveChangesAsync();

            var airline1 = await context.Airlines.FindAsync("airline", 11);
            Assert.Equal(airline, airline1);
        }
        finally
        {
            var entity = context.Remove(airline);
            await context.SaveChangesAsync();
        }
    }

    // Hardening: SaveChanges dispatches non-transactional writes concurrently
    // (Parallel.ForEachAsync). A failed write must still surface as a clean DbUpdateException,
    // not an AggregateException, matching the original serial behaviour.
    [Fact]
    public async Task Test_SaveChanges_FailedWrite_ThrowsDbUpdateException()
    {
        // Per-run unique id well above travel-sample's real airline ids, so the test never
        // overwrites or deletes pre-existing documents (even if a prior run's cleanup failed).
        var id = Random.Shared.Next(1_000_000, int.MaxValue);
        await using var context = travelSampleFixture.GetDbContext();
        var existing = new TravelSampleFixture.Airline
        {
            Type = "airline", Id = id, Callsign = "DUP", Country = "United States",
            Icao = "DUP", Iata = "D1", Name = "Duplicate Air"
        };
        try
        {
            // Ensure the key exists.
            context.Update(existing);
            await context.SaveChangesAsync();

            // Insert (Added) the same key in a fresh context → InsertAsync fails (DocumentExists).
            await using var context2 = travelSampleFixture.GetDbContext();
            context2.Add(new TravelSampleFixture.Airline
            {
                Type = "airline", Id = id, Callsign = "DUP2", Country = "United States",
                Icao = "DUP", Iata = "D2", Name = "Duplicate Air 2"
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => context2.SaveChangesAsync());
        }
        finally
        {
            context.Remove(existing);
            await context.SaveChangesAsync();
        }
    }

    // Isolates the ContosoUniversity seed loss: fire concurrent KV inserts into a collection that
    // was created moments earlier (as the AppHost does), then read each back by key (no index).
    // If any are missing, the SDK acknowledged an insert into a not-yet-ready collection that never
    // persisted. Varies the settle delay between collection creation and the inserts.
    [Theory]
    [InlineData(0)]
    [InlineData(2000)]
    public async Task Test_FreshCollection_ConcurrentInsert_PersistsAll(int settleDelayMs)
    {
        const int docCount = 100;
        var scopeName = bloggingFixture.ScopeName;
        var collectionName = "freshcoll" + Guid.NewGuid().ToString("N");

        using var cluster = await global::Couchbase.Cluster.ConnectAsync(
            bloggingFixture.Host, bloggingFixture.Username, bloggingFixture.Password);
        var bucket = await cluster.BucketAsync(bloggingFixture.BucketName);
        var manager = bucket.Collections;

        await manager.CreateCollectionAsync(scopeName, collectionName,
            new global::Couchbase.Management.Collections.CreateCollectionSettings());

        try
        {
            if (settleDelayMs > 0)
            {
                await Task.Delay(settleDelayMs);
            }

            var collection = (await bucket.ScopeAsync(scopeName)).Collection(collectionName);

            // Fire all inserts concurrently, like the provider's Parallel.ForEachAsync seed path.
            var insertResults = await Task.WhenAll(
                Enumerable.Range(0, docCount).Select(async i =>
                {
                    try { await collection.InsertAsync($"k{i}", new { index = i }); return true; }
                    catch { return false; }
                }));
            var insertsReportedOk = insertResults.Count(ok => ok);

            var found = 0;
            for (var i = 0; i < docCount; i++)
            {
                try { await collection.GetAsync($"k{i}"); found++; }
                catch (global::Couchbase.Core.Exceptions.KeyValue.DocumentNotFoundException) { }
            }

            outputHelper.WriteLine(
                $"settleDelayMs={settleDelayMs} inserted={docCount} insertsOk={insertsReportedOk} kvFound={found}");
            // Every insert must actually report success — otherwise a timeout/throw that still
            // persisted the doc could leave kvFound correct while masking a real reliability issue.
            Assert.Equal(docCount, insertsReportedOk);
            Assert.Equal(docCount, found);
        }
        finally
        {
            await manager.DropCollectionAsync(scopeName, collectionName);
        }
    }

    // Read-your-writes regression: on a freshly-created collection with a properly-built index
    // (both a PRIMARY index and a secondary GSI), a RequestPlus query issued immediately after a
    // burst of concurrent inserts must see every document. Guards the guarantee ContosoUniversity
    // relies on (the app configures RequestPlus). AtPlus (ConsistentWith mutation tokens) is
    // exercised too. Uses dedicated collections so unrelated churn can't perturb the index.
    [Fact]
    public async Task Test_ReadYourWrites_WithProperIndex_RequestPlusSeesAllWrites()
    {
        const int docCount = 100;
        const int rounds = 5;
        var scope = bloggingFixture.ScopeName;
        var bucketName = bloggingFixture.BucketName;
        var baseId = Random.Shared.Next(5_000_000, int.MaxValue - (rounds * docCount) - 1);

        var clusterOptions = new global::Couchbase.ClusterOptions()
            .WithConnectionString(bloggingFixture.Host)
            .WithCredentials(bloggingFixture.Username, bloggingFixture.Password);
        clusterOptions.EnableMutationTokens = true;
        using var cluster = await global::Couchbase.Cluster.ConnectAsync(clusterOptions);
        var bucket = await cluster.BucketAsync(bucketName);
        var manager = bucket.Collections;

        var primaryColl = "idxprim" + Guid.NewGuid().ToString("N");
        var gsiColl = "idxgsi" + Guid.NewGuid().ToString("N");

        async Task DropCollectionIfExists(string name)
        {
            try { await manager.DropCollectionAsync(scope, name); }
            catch (global::Couchbase.Management.Collections.CollectionNotFoundException) { }
        }

        async Task RunDdl(string s) { using var r = await cluster.QueryAsync<dynamic>(s); await foreach (var _ in r.Rows) { } }

        // Poll system:indexes until the two new indexes report state='online', instead of a fixed
        // sleep (which is flaky under load/slow CI).
        async Task WaitForIndexesOnlineAsync(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            var stmt = $"SELECT RAW COUNT(*) FROM system:indexes WHERE bucket_id = '{bucketName}' "
                + $"AND scope_id = '{scope}' AND keyspace_id IN ['{primaryColl}', '{gsiColl}'] AND state = 'online'";
            var online = 0;
            Exception? lastError = null;
            while (true)
            {
                try
                {
                    using var r = await cluster.QueryAsync<int>(stmt);
                    online = 0;
                    await foreach (var c in r.Rows) { online = c; break; }
                    if (online >= 2) return;
                    lastError = null;
                }
                catch (Exception ex) when (DateTime.UtcNow <= deadline)
                {
                    // Transient query failures are common right after DDL / on busy CI — keep
                    // retrying until the deadline instead of failing the whole test. Keep the last
                    // failure so a persistent one (malformed query, query service down) is surfaced.
                    lastError = ex;
                }

                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"Indexes not online within {timeout} ({online}/2).", lastError);
                await Task.Delay(500);
            }
        }

        var failures = new List<string>();
        try
        {
            // Create both collections inside the try so a failure on the second still cleans up the
            // first (see finally); a leaked collection would otherwise accumulate across runs.
            await manager.CreateCollectionAsync(scope, primaryColl, new global::Couchbase.Management.Collections.CreateCollectionSettings());
            await manager.CreateCollectionAsync(scope, gsiColl, new global::Couchbase.Management.Collections.CreateCollectionSettings());

            await RunDdl($"CREATE PRIMARY INDEX ON `{bucketName}`.`{scope}`.`{primaryColl}`");
            await RunDdl($"CREATE INDEX `ix_blogId` ON `{bucketName}`.`{scope}`.`{gsiColl}`(blogId)");
            await WaitForIndexesOnlineAsync(TimeSpan.FromSeconds(30));

            var scopeObj = await bucket.ScopeAsync(scope);
            var primary = scopeObj.Collection(primaryColl);
            var gsi = scopeObj.Collection(gsiColl);

            async Task Verify(global::Couchbase.KeyValue.ICouchbaseCollection coll, string collName, int start)
            {
                var ids = Enumerable.Range(start, docCount).ToList();
                var results = await Task.WhenAll(ids.Select(id =>
                    coll.InsertAsync(id.ToString(), new { blogId = id })));
                var ms = global::Couchbase.Query.MutationState.From(results);
                var stmt = $"SELECT RAW b.blogId FROM `{bucketName}`.`{scope}`.`{collName}` b WHERE b.blogId >= {start} AND b.blogId < {start + docCount}";

                // Count via await foreach (drains the result) rather than an async-LINQ CountAsync,
                // which collides between System.Linq.AsyncEnumerable and the provider's extension.
                int rp = 0, at = 0;
                using (var rpResult = await cluster.QueryAsync<int>(stmt, new global::Couchbase.Query.QueryOptions()
                    .ScanConsistency(global::Couchbase.Query.QueryScanConsistency.RequestPlus)))
                {
                    await foreach (var _ in rpResult.Rows) { rp++; }
                }
                using (var atResult = await cluster.QueryAsync<int>(stmt, new global::Couchbase.Query.QueryOptions()
                    .ConsistentWith(ms)))
                {
                    await foreach (var _ in atResult.Rows) { at++; }
                }

                if (rp != docCount) failures.Add($"{collName} requestPlus={rp}/{docCount}");
                if (at != docCount) failures.Add($"{collName} atPlus={at}/{docCount}");
            }

            for (var round = 0; round < rounds; round++)
            {
                await Verify(primary, primaryColl, baseId + round * docCount);
                await Verify(gsi, gsiColl, baseId + round * docCount);
            }
        }
        finally
        {
            await DropCollectionIfExists(primaryColl);
            await DropCollectionIfExists(gsiColl);
        }

        Assert.True(failures.Count == 0, "read-your-writes shortfalls: " + string.Join("; ", failures));
    }

    // The provider threads the CancellationToken through the KV write path; a cancelled save must
    // surface as an OperationCanceledException, not a wrapped DbUpdateException.
    [Fact]
    public async Task Test_SaveChanges_HonorsCancellation()
    {
        await using var context = travelSampleFixture.GetDbContext();
        context.Add(new TravelSampleFixture.Airline
        {
            Type = "airline", Id = Random.Shared.Next(1_000_000, int.MaxValue),
            Callsign = "CANCEL", Country = "United States", Icao = "CXL", Iata = "CX", Name = "Cancel Air"
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.SaveChangesAsync(cts.Token));
    }

    // Directly exercises that the CancellationToken is threaded into the KV call (not just observed
    // by EF/Parallel scheduling): warm the collection cache first so the cancelled call skips the
    // semaphore and goes straight to the SDK operation, which must surface OperationCanceledException.
    [Fact]
    public async Task Test_ClientWrapper_ThreadsCancellationIntoKvCall()
    {
        await using var context = bloggingFixture.GetDbContext();
        var wrapper = context.GetService<ICouchbaseClientWrapper>();
        var keyspace = $"{wrapper.BucketName}.blogs.blog";
        var id = $"ct-test-{Guid.NewGuid():N}";

        // Warm the keyspace cache (and create a document to remove).
        await wrapper.CreateDocument(id, keyspace, new { type = "ct-test" });
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => wrapper.DeleteDocument(id, keyspace, cts.Token));
        }
        finally
        {
            // Best-effort cleanup (the cancelled delete above should not have removed the document).
            try { await wrapper.DeleteDocument(id, keyspace); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Test_RemoveAsync()
    {
        await using var context = travelSampleFixture.GetDbContext();
        var airline = new TravelSampleFixture.Airline
        {
            Type = "airline",
            Id = 11,
            Callsign = "MILE-AIR",
            Country = "United States",
            Icao = "MLA",
            Iata = "Q5",
            Name = "40-Mile Air"
        };

        context.Add(airline);
        await context.SaveChangesAsync();

        context.Remove(airline);
        await context.SaveChangesAsync();

        var airline1 = await context.Airlines.FindAsync("airline", 11);
        Assert.Null(airline1);
    }

    [Fact]
    public async Task Test_UpdateAsync()
    {
        await using var context = travelSampleFixture.GetDbContext();
        var airline = new TravelSampleFixture.Airline
        {
            Type = "airline",
            Id = 11,
            Callsign = "MILE-AIR",
            Country = "United States",
            Icao = "MLA",
            Iata = "Q5",
            Name = "40-Mile Air"
        };
        try
        {
            context.Add(airline);
            await context.SaveChangesAsync();

            var airline1 = await context.Airlines.FindAsync("airline", 11);
            airline1.Name = "bob";
            context.Update(airline1);

            await context.SaveChangesAsync();
            var airlineChanged = await context.Airlines.FindAsync("airline", 11);
            Assert.Equal("bob", airlineChanged.Name);
        }
        finally
        {
            context.Remove(airline);
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Test_AddAsyncAsync()
    {
        await using var context = travelSampleFixture.GetDbContext();
        var airline = new TravelSampleFixture.Airline
        {
            Type = "airline",
            Id = 11,
            Callsign = "MILE-AIR",
            Country = "United States",
            Icao = "MLA",
            Iata = "Q5",
            Name = "40-Mile Air"
        };
        try
        {
            context.Update(airline);
            await context.SaveChangesAsync();

            var airline1 = await context.Airlines.FindAsync("airline", 11);
            Assert.Equal(airline, airline1);
        }
        finally
        {
            context.Remove(airline);
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Test_SaveChanges()
    {
        await using var context = bloggingFixture.GetDbContext();
        context.Blogs.Add(new BloggingFixture.Blog
        {
            BlogId = Guid.NewGuid().GetHashCode(), Url = "http://example.com"
        });
        await context.SaveChangesAsync();
        var blog = await context.Blogs.FirstAsync(b => b.Url == "http://example.com");
        blog.Url = "http://example.com/blog";
        await context.SaveChangesAsync();
        context.Remove(blog);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Test_Saving_Related_Data()
    {
        await using var context = bloggingFixture.GetDbContext();
        var blog = new BloggingFixture.Blog
        {
            BlogId = Guid.NewGuid().GetHashCode(),
            Url = "http://blogs.msdn.com/dotnet",
            Posts =
            [
                new() { Title = "Intro to C#", PostId = Guid.NewGuid().GetHashCode() },
                new() { Title = "Intro to VB.NET", PostId = Guid.NewGuid().GetHashCode() },
                new() { Title = "Intro to F#", PostId = Guid.NewGuid().GetHashCode() }
            ]
        };

        try
        {
            await context.Blogs.AddAsync(blog);
            var count = await context.SaveChangesAsync();
            Assert.Equal(4, count);
        }
        finally
        {
            context.Remove(blog);
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Test_Adding_Related_Entity()
    {
        await using var context = bloggingFixture.GetDbContext();
        context.Update(new BloggingFixture.Blog{ BlogId = 10, Url = "http://example.com" });
        context.Update(new BloggingFixture.Post { BlogId = 10, PostId = 10 });
        await context.SaveChangesAsync();

        var blog = await context.Blogs.FirstAsync();
        blog.Posts = await context.Posts.Where(x => x.BlogId == blog.BlogId).ToListAsync();

        var post = new BloggingFixture.Post { Title = "Intro to EF Core", PostId = 11};
        blog.Posts.Add(post);
        await context.SaveChangesAsync();

        blog.Posts.Remove(post);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Test_Changing_Relationships()
    {
        await using var context = bloggingFixture.GetDbContext();
        context.Update(new BloggingFixture.Post { Title = "Intro to EF Core", PostId = 4, BlogId = 202 });
        await context.SaveChangesAsync();
        var blog1 = await context.Blogs.FirstAsync();

        var blog = new BloggingFixture.Blog { Url = "http://blogs.msdn.com/visualstudio", BlogId = 202};
        var post = await context.Posts.FirstAsync();

        post.Blog = blog;
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Removing_Relationships_Async()
    {
        await using var context = bloggingFixture.GetDbContext();
        var blog = new BloggingFixture.Blog { Url = "http://blogs.msdn.com/visualstudio", BlogId = 2 };
        var post = new BloggingFixture.Post { Title = "Intro to EF Core", PostId = 4, BlogId = 2 };
        try
        {
            context.Update(blog);
            await context.SaveChangesAsync();

            context.Update(post);
            await context.SaveChangesAsync();

            blog = await context.Blogs.FirstAsync();
            var posts = await context.Posts.ToListAsync();
            blog.Posts = await context.Posts.Where(x => x.BlogId == blog.BlogId).ToListAsync();
            post = blog.Posts.First();
        }
        catch (Exception ex)
        {
            outputHelper.WriteLine(ex.Message);
        }
        finally
        {

            blog.Posts.Remove(post);
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Test_Hotel_Query()
    {
        await using var context = travelSampleFixture.GetDbContext();

        // Query hotels from the travel-sample bucket
        var hotels = await context.Hotels.Take(10).ToListAsync();

        Assert.NotEmpty(hotels);

        // Verify basic hotel properties are populated
        var hotel = hotels.First();
        Assert.NotNull(hotel.Id);
        Assert.NotNull(hotel.Type);
        Assert.Equal("hotel", hotel.Type);
    }

    [Fact(Skip = "Hotel.Geo is mapped with .Ignore() in TravelSampleDbContext. Map it as OwnsOne to read the embedded 'geo' object (owned types are supported; lat/lon/accuracy match the camelCase convention).")]
    public async Task Test_Hotel_With_Geo()
    {
        await using var context = travelSampleFixture.GetDbContext();

        // Query any hotel - Geo is ignored by EF so not included in query projection
        var hotels = await context.Hotels.Take(10).ToListAsync();

        // Find one with geo data
        var hotel = hotels.FirstOrDefault(h => h.Geo != null);

        Assert.NotNull(hotel);
        Assert.NotNull(hotel.Geo);
        Assert.NotNull(hotel.Geo.Lat);
        Assert.NotNull(hotel.Geo.Lon);
    }

    [Fact(Skip = "Hotel.Reviews is mapped with .Ignore() in TravelSampleDbContext. Map as OwnsMany to enable (owned types are supported). Note: the nested 'ratings' field names (e.g. 'Service', 'Check in / front desk') don't match the naming convention and would need explicit column mapping.")]
    public async Task Test_Hotel_With_Reviews()
    {
        await using var context = travelSampleFixture.GetDbContext();

        // Query any hotel - Reviews is ignored by EF so not included in query projection
        var hotels = await context.Hotels.Take(10).ToListAsync();

        // Find one with reviews
        var hotel = hotels.FirstOrDefault(h => h.Reviews != null && h.Reviews.Count > 0);

        Assert.NotNull(hotel);
        Assert.NotNull(hotel.Reviews);
        Assert.NotEmpty(hotel.Reviews);

        // Verify review properties
        var review = hotel.Reviews.First();
        Assert.NotNull(review.Author);
        Assert.NotNull(review.Content);
    }

    [Fact]
    public async Task Test_Hotel_Crud()
    {
        await using var context = travelSampleFixture.GetDbContext();

        // Note: Geo and Reviews are ignored by EF Core, so only scalar properties are persisted
        var hotel = new TravelSampleFixture.Hotel
        {
            Id = 99999,
            Type = "hotel",
            Name = "Test Hotel",
            City = "Test City",
            Country = "Test Country",
            Description = "A test hotel for integration testing"
        };

        try
        {
            // Create
            context.Hotels.Add(hotel);
            var inserted = await context.SaveChangesAsync();
            Assert.Equal(1, inserted);

            // Read
            var retrievedHotel = await context.Hotels.FindAsync(hotel.Id);
            Assert.NotNull(retrievedHotel);
            Assert.Equal("Test Hotel", retrievedHotel.Name);
            Assert.Equal("Test City", retrievedHotel.City);

            // Update
            retrievedHotel.Name = "Updated Test Hotel";
            context.Hotels.Update(retrievedHotel);
            await context.SaveChangesAsync();

            var updatedHotel = await context.Hotels.FindAsync(hotel.Id);
            Assert.Equal("Updated Test Hotel", updatedHotel?.Name);
        }
        finally
        {
            // Delete
            context.Hotels.Remove(hotel);
            await context.SaveChangesAsync();

            var deletedHotel = await context.Hotels.FindAsync(hotel.Id);
            Assert.Null(deletedHotel);
        }
    }
}