using Xunit.Abstractions;
using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

[Collection(CouchbaseTestingCollection.Name)]
public class QueryTests(
    BloggingFixture bloggingFixture,
    TravelSampleFixture travelSampleFixture,
    ITestOutputHelper outputHelper)
{
    [Fact]
    public async Task Test_Select_Limit()
    {
        await using var context = travelSampleFixture.GetDbContext();
        var results = await context.Airlines.OrderBy(x => x.Id).Take(10).ToListAsync();
        foreach (var airline in results)
        {
            outputHelper.WriteLine(airline.ToString());
        }
    }

    [Fact]
    public async Task Test_Skip_And_Take()
    {
        await using var context = travelSampleFixture.GetDbContext();
        var pageIndex = 1;
        var pageSize = 10;
        var items = await context.Airlines.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToListAsync();
        Assert.Equal(10, items.Count);
    }

    [Fact]
    public async Task Test_Count()
    {
        await using var context = travelSampleFixture.GetDbContext();
        var count = await context.Airlines.CountAsync();
        Assert.True(count != 0);
    }

    [Fact]
    public async Task Test_Select_Linq()
    {
       await using var context = travelSampleFixture.GetDbContext();
       var airlines = from a in context.Airlines
            select a;

        var count = await airlines.CountAsync();
        Assert.True(count > 0);
    }

    [Fact]
    public async Task Test()
    {
        await using var context = travelSampleFixture.GetDbContext();
        var airline = new TravelSampleFixture.Airline
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
              .OrderBy(x => x.Id).ToListAsync<TravelSampleFixture.Airline>(); 

        foreach (var a in airlines1)
        {
              outputHelper.WriteLine(a.ToString());
        }

        var airlines = await context.Airlines
              .OrderBy(x => x.Id)
              .FirstAsync();

        outputHelper.WriteLine(airlines.ToString());

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Test_Pagination()
    {
        await using var context = bloggingFixture.GetDbContext();
        var position = 1;
        var nextPage = await context.PersonPhotos
            .OrderBy(s => s.PersonPhotoId)
            .Skip(position)
            .Take(10)
            .ToListAsync();

        Assert.Equal(2, nextPage.First().PersonPhotoId);
    }

    [Fact]
    public async Task Test_Simple_Joins()
    {
        await using var context = bloggingFixture.GetDbContext();
        context.Update(
            new BloggingFixture.Person
            {
                PersonId = 1,
                PhotoId = 1,
                Name = "John Doe"
            });
        context.Update(
            new BloggingFixture.PersonPhoto
            {
                Caption = "This is a photo.",
                Person = await context.People.FindAsync(1),
                PersonPhotoId = 1
            });

        await context.SaveChangesAsync();
        var query = from photo in context.Set<BloggingFixture.PersonPhoto>()
            join person in context.Set<BloggingFixture.Person>()
                on photo.PersonPhotoId equals person.PhotoId
            select new { person, photo };

        Assert.True(await query.AnyAsync());
    }

    [Fact]
    public async Task Test_Count_Group_OrderBy()
    {
        await using var context = bloggingFixture.GetDbContext();
        var query = from p in context.Set<BloggingFixture.Post>()
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
        await using var context = bloggingFixture.GetDbContext();
        var query = from p in context.Set<BloggingFixture.Post>()
            group p by p.AuthorId
            into g
            select new { g.Key, Count = g.LongCount() };

        var result = await query.ToListAsync();
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Test_Max()
    {
        await using var context = bloggingFixture.GetDbContext();
        var query = from p in context.Set<BloggingFixture.Post>()
            group p by p.AuthorId
            into g
            select new { g.Key, Max=g.Max(x=>x.Rating) };

        var result = await query.ToListAsync();
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Test_Min()
    {
        await using var context = bloggingFixture.GetDbContext();
        var query = from p in context.Set<BloggingFixture.Post>()
            group p by p.AuthorId
            into g
            select new { g.Key, Min=g.Min(x=>x.Rating) };

        var result = await query.ToListAsync();
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Test_Sum()
    {
        await using var context = bloggingFixture.GetDbContext();
        var query = from p in context.Set<BloggingFixture.Post>()
            group p by p.AuthorId
            into g
            select new { g.Key, Sum=g.Sum(x=>x.Rating) };

        var result = await query.ToListAsync();
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task Test_Average_Does_Not_Throw_CouchbaseParsingException()
    {
        await using var context = bloggingFixture.GetDbContext();
        var query = from p in context.Set<BloggingFixture.Post>()
            group p by p.AuthorId
            into g
            select new { g.Key, Avg=g.Average(x=>x.Rating) };

        var result = await query.ToListAsync();
        Assert.NotEmpty(result);
    }
}
