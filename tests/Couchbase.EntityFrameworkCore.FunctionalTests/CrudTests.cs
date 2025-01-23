using ContosoUniversity.Models;
using Couchbase.Core.IO.Operations;
using Couchbase.Core.IO.Transcoders;
using Couchbase.EntityFrameworkCore.FunctionalTests.Fixtures;
using Couchbase.EntityFrameworkCore.FunctionalTests.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCore.FunctionalTests;

[Collection(CouchbaseTestingCollection.Name)]
public class CrudTests
{
    private readonly CouchbaseFixture _couchbaseFixture;
    private readonly ITestOutputHelper _outputHelper;

    public CrudTests(CouchbaseFixture couchbaseFixture, ITestOutputHelper outputHelper)
    {
        _couchbaseFixture = couchbaseFixture;
        _outputHelper = outputHelper;
    }

    [Fact]
    public async Task Test_ExecuteUpdate()
    {
        var context = _couchbaseFixture.TravelSampleContext;
        var airline1 = new Airline
        {
            Type = "airline",
            Id = 666,
            Callsign = "MILE-AIR",
            Country = "United States",
            Icao = "MLA",
            Iata = "Q5",
            Name = "40-Mile Air"
        };

        var airline2 = new Airline
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
            context.Add(airline1);
            context.Add(airline2);

            var inserted = await context.SaveChangesAsync();
            Assert.Equal(2, inserted);

            var count = context.Airlines.Count(a => a.Id > 665 && a.Id < 668);
            Assert.Equal(2, count);

            await context.Airlines
                .Where(a => a.Id > 665 && a.Id < 668)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(a => a.Country, "Banana Republic"));

            var count2 = context.Airlines.Count(a => a.Country == "Banana Republic");
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
        var context = _couchbaseFixture.TravelSampleContext;
        var airline1 = new Airline
        {
            Type = "airline",
            Id = 777,
            Callsign = "MILE-AIR",
            Country = "United States",
            Icao = "MLA",
            Iata = "Q5",
            Name = "40-Mile Air"
        };

        var airline2 = new Airline
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
            context.Add(airline1);
            context.Add(airline2);

            await context.SaveChangesAsync();
            var count = context.Airlines.Count(a => a.Id > 776 && a.Id < 779);
            Assert.Equal(2, count);

            await context.Airlines
                .Where(a => a.Id > 776 && a.Id < 779)
                .ExecuteDeleteAsync();

            var count2 = context.Airlines.Count(a => a.Id > 776 && a.Id < 779);
            Assert.Equal(0, count2);
        }
        finally
        {
            context.Remove(airline1);
            context.Remove(airline2);
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Test_ComplexObject()
    {
        var context = _couchbaseFixture.TravelSampleContext;

        var user = new User
        {
            Name = "Jeff Morris",
            DrivingLicence = "A^&*GUIOO",
            PreferredEmail = "jefry@job.com",
            Addresses =
            [
                new Address
                {
                    City = "Huntington Beach", Country = "USA", HomeAddress = "10032 Stonybrook Drive", ID = "1",
                    Type = "Home"
                },
                new Address
                {
                    City = "Newport", ID = "2", HomeAddress = "123 Balboa Ave", Country = "USA", Type = "Work"
                }
            ]
        };
        await context.AddAsync(user);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Test_AddAsync()
    {
        var context = _couchbaseFixture.TravelSampleContext;
        var airline = new Airline
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
            Assert.Equal(airline, airline1);
        }
        finally
        {
            context.Remove(airline);
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Test_RemoveAsync()
    {
        var context = _couchbaseFixture.TravelSampleContext;
        var airline = new Airline
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
        var context = _couchbaseFixture.TravelSampleContext;
        var airline = new Airline
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
        var context = _couchbaseFixture.TravelSampleContext;
        var airline = new Airline
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
            await context.AddAsync(airline);
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
        using (var context = new BloggingContext())
        {
            context.Blogs.Add(new Blog
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
    }

    [Fact]
    public async Task Test_Saving_Related_Data()
    {
        using (var context = new BloggingContext())
        {
            var blog = new Blog
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

            await context.Blogs.AddAsync(blog);
            var count = await context.SaveChangesAsync();
            Assert.Equal(4, count);
            context.Remove(blog);
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Test_Adding_Related_Entity()
    {
        using (var context = new BloggingContext())
        {
            context.AddAsync(new Blog{ BlogId = 10, Url = "http://example.com" });
            context.AddAsync(new Post { BlogId = 10, PostId = 10 });
            await context.SaveChangesAsync();

            var blog = context.Blogs.First();
            blog.Posts = context.Posts.Where(x => x.BlogId == blog.BlogId).ToList();
            var post = new Post { Title = "Intro to EF Core", PostId = 11};

            blog.Posts.Add(post);
            context.SaveChanges();

            blog.Posts.Remove(post);
            context.SaveChanges();
        }
    }

    [Fact]
    public async Task Test_Changing_Relationships()
    {
        using (var context = new BloggingContext())
        {
            context.Add(new Post { Title = "Intro to EF Core", PostId = 4, BlogId = 2 });
            await context.SaveChangesAsync();

            var blog = new Blog { Url = "http://blogs.msdn.com/visualstudio", BlogId = 2};
            var post = context.Posts.First();

            post.Blog = blog;
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Removing_Relationships()
    {
        using (var context = new BloggingContext())
        {
            var blog = new Blog { Url = "http://blogs.msdn.com/visualstudio", BlogId = 2 };
            var post = new Post { Title = "Intro to EF Core", PostId = 4, BlogId = 2 };
            try
            {
                context.Add(blog);
                await context.SaveChangesAsync();

                context.Add(post);
                await context.SaveChangesAsync();

                blog = context.Blogs.First();
                var posts = context.Posts.ToList();
                blog.Posts = context.Posts.Where(x => x.BlogId == blog.BlogId).ToList();
                post = blog.Posts.First();
            }
            finally
            {

                blog.Posts.Remove(post);
                await context.SaveChangesAsync();
            }
        }
    }
}