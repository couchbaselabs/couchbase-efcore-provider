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
    private readonly CouchbaseFixture _couchbaseFixture;
    private readonly ITestOutputHelper _outputHelper;

    public QueryTests(CouchbaseFixture couchbaseFixture,  ITestOutputHelper outputHelper)
    {
        _couchbaseFixture = couchbaseFixture;
        _outputHelper = outputHelper;
    }

    [Fact]
    public async Task Test_Select_Limit()
    {
        var context = _couchbaseFixture.TravelSampleContext;
        var results = context.Airlines.OrderBy(x => x.Id).Take(10);
        foreach (var airline in results)
        {
            _outputHelper.WriteLine(airline.ToString());
        }
    }

    [Fact]
    public void Test_Skip_And_Take()
    {
        var context = _couchbaseFixture.TravelSampleContext;

        var pageIndex = 0;
        var pageSize = 10;
        var items = context.Airlines.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToListAsync();
        Assert.Equal(10, items.Result.Count);
    }

    [Fact]
    public void Test_Count()
    {
        var context = _couchbaseFixture.TravelSampleContext;
        var count = context.Airlines.Count();
    }

    [Fact]
    public async Task Test_Select_Linq()
    {
        var context = _couchbaseFixture.TravelSampleContext;
        var airlines = from a in context.Airlines
            select a;

        var count = await airlines.CountAsync();
        Assert.Equal(188, count);
    }

    [Fact]
    public async Task Test()
    {
        var context = _couchbaseFixture.TravelSampleContext;
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

        context.Add(airline);
        airline.Name = "foo";

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
        var context = new SessionContext(new ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithCredentials("Administrator", "password"));

        context.Add(new Session
        {
            Category = "disabled",
            SessionId = 1,
            TenantId = "1"
        });
        context.Add(new Session
        {
            Category = "xydisabled",
            SessionId = 2,
            TenantId = "2"
        });

        var count = await context.SaveChangesAsync();

        var position = 2;
        var nextPage = context.Sessions
            .OrderBy(s => s.Id)
            .Skip(position)
            .Take(10)
            .ToList();
        _outputHelper.WriteLine(nextPage.First().Category);
    }

    [Fact]
    public async Task Test_FindAsync()
    {
        var context = new SessionContext(new ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithCredentials("Administrator", "password"));

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
            context.Add(session);
            var found = await context.FindAsync<Session>(pkey);
            Assert.Equal(found.Id, pkey);
        }
        finally
        {
            context.Remove(session);
        }
    }

    [Fact]
    public async Task Test_SqlQueries_Throws_NotImplementedException()
    {
        var context = new BloggingContext();
        var rating = 3;

        await Assert.ThrowsAsync<NotImplementedException>(async () => await context.Blogs
            .FromSqlRaw($"SELECT VALUE c FROM root c WHERE c.Rating > {rating}")
            .ToListAsync());
    }
    
    [Fact]
    public async Task Test_Simple_Joins()
    {
        var context = new BloggingContext();
        var query = from photo in context.Set<PersonPhoto>()
            join person in context.Set<Person>()
                on photo.PersonPhotoId equals person.PhotoId
            select new { person, photo };

        var results = await query.ToListAsync();
        Assert.True(query.Any());
    }

    [Fact]
    public async Task Test_Count_Group_OrderBy()
    {
        var context = new BloggingContext();
        var query = from p in context.Set<Post>()
            group p by p.AuthorId
            into g
            where g.Count() > 0
            orderby g.Key
            select new { g.Key, Count = g.Count() };

        var result = await query.ToListAsync();
    }

    [Fact]
    public async Task Test_LongCount()
    {
        var context = new BloggingContext();
        var query = from p in context.Set<Post>()
            group p by p.AuthorId
            into g
            select new { g.Key, Count = g.LongCount() };

        var result = await query.ToListAsync();
    }

    [Fact]
    public async Task Test_Max()
    {
        var context = new BloggingContext();
        var query = from p in context.Set<Post>()
            group p by p.AuthorId
            into g
            select new { g.Key, Max=g.Max(x=>x.Rating) };

        var result = await query.ToListAsync();
    }

    [Fact]
    public async Task Test_Min()
    {
        var context = new BloggingContext();
        var query = from p in context.Set<Post>()
            group p by p.AuthorId
            into g
            select new { g.Key, Min=g.Min(x=>x.Rating) };

        var result = await query.ToListAsync();
    }

    [Fact]
    public async Task Test_Sum()
    {
        var context = new BloggingContext();
        var query = from p in context.Set<Post>()
            group p by p.AuthorId
            into g
            select new { g.Key, Sum=g.Sum(x=>x.Rating) };

        var result = await query.ToListAsync();
    }

    [Fact]
    public async Task Test_Average_Throws_CouchbaseParsingException()
    {
        var context = new BloggingContext();
        var query = from p in context.Set<Post>()
            group p by p.AuthorId
            into g
            select new { g.Key, Avg=g.Average(x=>x.Rating) };

         //Ticket for fixing: https://jira.issues.couchbase.com/browse/NCBC-3891
         await Assert.ThrowsAsync<ParsingFailureException>(async ()=> await query.ToListAsync());
    }

    [Fact]
    public async Task Test_FromSqlRaw()
    {
        var context = new BloggingContext();
        string query = "SELECT p.* FROM `Content`.`Blogs`.`Person` as p WHERE PersonId={0}";
        var person = await context.Set<Person>()
            .FromSqlRaw(query, 1)
            .FirstOrDefaultAsync();
    }

    [Fact]
    public async Task Test_FromRaw_Throws_NotImplementedException()
    {
        var context = new BloggingContext();
        string query = "SELECT p.* FROM `Content`.`Blogs`.`Person` as p WHERE PersonId={0}";
        Assert.Throws<NotImplementedException>(()=>context.Blogs
            .FromSql($"SELECT * FROM `Content`.`Blogs`.`Blog`")
            .ToList());
    }
}