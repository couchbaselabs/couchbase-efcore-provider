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