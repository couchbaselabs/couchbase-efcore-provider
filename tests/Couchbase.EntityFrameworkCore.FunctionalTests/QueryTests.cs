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
        var context = _couchbaseFixture.GetDbContext;
        var results = context.Airlines.OrderBy(x => x.Id).Take(10);
        foreach (var airline in results)
        {
            _outputHelper.WriteLine(airline.ToString());
        }
    }

    [Fact]
    public void Test_Skip_And_Take()
    {
        var context = _couchbaseFixture.GetDbContext;

        var pageIndex = 0;
        var pageSize = 10;
        var items = context.Airlines.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToListAsync();
        Assert.Equal(10, items.Result.Count);
    }

    [Fact]
    public void Test_Count()
    {
        var context = _couchbaseFixture.GetDbContext;
        var count = context.Airlines.Count();
    }

    [Fact]
    public async Task Test_Select_Linq()
    {
        var context = _couchbaseFixture.GetDbContext;
        var airlines = from a in context.Airlines
            select a;

        var count = await airlines.CountAsync();
        Assert.Equal(188, count);
    }

    [Fact]
    public async Task Test()
    {
        var context = _couchbaseFixture.GetDbContext;
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
        airline.Name = "foo";
        //context.Update(airline);
        
      //  var found = context.Find<Airline>("airline", 11);
      
      var ab = await context.Airlines.FindAsync("airline", 11);
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
        
       // context.Remove(airline);
        await context.SaveChangesAsync();
    }
}