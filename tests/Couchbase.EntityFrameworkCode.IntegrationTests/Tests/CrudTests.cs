using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
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

    [Fact(Skip = "This test requires customizing travel-sample with airline and user collections.")]
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

    [Fact]
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

    [Fact(Skip = "Nested objects (Geo) are ignored by EF Core - requires document-oriented query support")]
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

    [Fact(Skip = "Nested collections (Reviews) are ignored by EF Core - requires document-oriented query support")]
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