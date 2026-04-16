using Couchbase.Core.Exceptions;
using Couchbase.EntityFrameworkCore.FunctionalTests.Fixtures;
using Couchbase.EntityFrameworkCore.FunctionalTests.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCore.FunctionalTests;

[Collection(CouchbaseTestingCollection.Name)]
public class QueryTests
{
    private readonly BloggingFixture _bloggingFixture;
    private readonly TravelSampleFixture _travelSampleFixture;
    private readonly SessionFixture _sessionFixture;
    private readonly ITestOutputHelper _outputHelper;

    public QueryTests(BloggingFixture bloggingFixture,
        TravelSampleFixture travelSampleFixture,
        SessionFixture sessionFixture,
        ITestOutputHelper outputHelper)
    {
        _bloggingFixture = bloggingFixture;
        _travelSampleFixture = travelSampleFixture;
        _sessionFixture = sessionFixture;
        _outputHelper = outputHelper;
    }

    [Fact]
    public async Task Test_Select_Limit()
    {
        await using var context = _travelSampleFixture.CreateDbContext();
        var results = await context.Airlines.OrderBy(x => x.Id).Take(10).ToListAsync();
        foreach (var airline in results)
        {
            _outputHelper.WriteLine(airline.ToString());
        }
    }

    [Fact]
    public async Task Test_Skip_And_Take()
    {
        await using var context = _travelSampleFixture.CreateDbContext();
        var pageIndex = 0;
        var pageSize = 10;
        var items = context.Airlines.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToListAsync();
        Assert.Equal(10, items.Result.Count);
    }

    [Fact]
    public async Task Test_Count()
    {
        await using var context = _travelSampleFixture.CreateDbContext();
        var count = await context.Airlines.CountAsync().ConfigureAwait(false);
        Assert.True(count != 0);
    }

    [Fact]
    public async Task Test_Select_Linq()
    {
       await using var context = _travelSampleFixture.CreateDbContext();
       var addresses = from a in context.Address
            select a;

        var count = await addresses.CountAsync();
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Test()
    {
        await using var context = _travelSampleFixture.CreateDbContext();
        var airline = new Airline
        {
            Type = "airline",
            Id = 1100000,
            Callsign = "MILE-AIR",
            Country = "United States",
            Icao = "MLA",
            Iata = "Q5",
            Name = "40-Mile Air"
        };

        context.Update(airline);
        airline.Name = "foo";
        
        var airlines3 =
            await context.Airlines
                .Where(o => o.Name == "40-Mile Air")
                .ToListAsync();

        var airlines1 = await context.Airlines
              .OrderBy(x => x.Id).ToListAsync<Airline>(); 

        foreach (var a in airlines1)
        {
              _outputHelper.WriteLine(a.ToString());
        }

        var airlines = await context.Airlines
              .OrderBy(x => x.Id)
              .FirstAsync();

        _outputHelper.WriteLine(airlines.ToString());

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Test_Pagination()
    {
        await using var context = _sessionFixture.CreateDbContext();

        var s1 = new Session
        {
            Category = "disabled",
            SessionId = 1,
            TenantId = "1"
        };
        context.Add(s1);

        var s2 = new Session
        {
            Category = "xydisabled",
            SessionId = 2,
            TenantId = "2"
        };
        context.Add(s2);

        try
        {
            var count = await context.SaveChangesAsync();

            var position = 1;
            var nextPage = await context.Sessions
                .OrderBy(s => s.Category)
                .Skip(position)
                .Take(10)
                .ToListAsync();
            Assert.Equal(s2.Category, nextPage.First().Category);
        }
        finally
        {
            context.RemoveRange(s1, s2);
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Test_FindAsync()
    {
        await using var context = _sessionFixture.CreateDbContext();

        var pkey = Guid.NewGuid();
        var session = new Session
        {
            Id = pkey,
            Category = "disabled",
            SessionId = 1,
            TenantId = "1"
        };

        try
        {
            var entityEntry = context.Update(session);
            await context.SaveChangesAsync();
            var found = await context.FindAsync<Session>(pkey);
            Assert.Equal(found.Id, pkey);
        }
        finally
        {
            var entityEntry = context.Remove(session);
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Test_Simple_Joins()
    {
        await using var context = _bloggingFixture.CreateDbContext();

        context.Update(
            new Person
            {
                PersonId = 1,
                PhotoId = 1,
                Name = "John Doe"
            });
        context.Update(
            new PersonPhoto
            {
                Caption = "This is a photo.",
                Person = await context.People.FindAsync(1),
                PersonPhotoId = 1
            });

        await context.SaveChangesAsync();
        var query = from photo in context.Set<PersonPhoto>()
            join person in context.Set<Person>()
                on photo.PersonPhotoId equals person.PhotoId
            select new { person, photo };

        Assert.True(await query.AnyAsync());
    }

    [Fact]
    public async Task Test_Count_Group_OrderBy()
    {
        await using var context = new BloggingContext();
        var query = from p in context.Set<Post>()
           group p by p.AuthorId
           into g
           where g.Count() > 0
           orderby g.Key
           select new { g.Key, Count = g.Count() };

       var result = await query.ToListAsync();
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Test_LongCount()
    {
        await using var context = new BloggingContext();
        var query = from p in context.Set<Post>()
            group p by p.AuthorId
            into g
            select new { g.Key, Count = g.LongCount() };

        var result = await query.ToListAsync();
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Test_Max()
    {
        await using var context = new BloggingContext();
        var query = from p in context.Set<Post>()
            group p by p.AuthorId
            into g
            select new { g.Key, Max=g.Max(x=>x.Rating) };

        var result = await query.ToListAsync();
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Test_Min()
    {
        await using var context = new BloggingContext();
        var query = from p in context.Set<Post>()
            group p by p.AuthorId
            into g
            select new { g.Key, Min=g.Min(x=>x.Rating) };

        var result = await query.ToListAsync();
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Test_Sum()
    {
        await using var context = new BloggingContext();
        var query = from p in context.Set<Post>()
            group p by p.AuthorId
            into g
            select new { g.Key, Sum=g.Sum(x=>x.Rating) };

        var result = await query.ToListAsync();
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Test_Average_Does_Not_Throw_CouchbaseParsingException()
    {
        await using var context = new BloggingContext();
        var query = from p in context.Set<Post>()
            group p by p.AuthorId
            into g
            select new { g.Key, Avg=g.Average(x=>x.Rating) };

        var result = await query.ToListAsync();
        Assert.NotEmpty(result);
    }
}
