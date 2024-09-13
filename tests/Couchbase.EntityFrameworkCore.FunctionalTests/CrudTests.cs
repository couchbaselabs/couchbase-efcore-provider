using Couchbase.EntityFrameworkCore.FunctionalTests.Fixtures;
using Couchbase.EntityFrameworkCore.FunctionalTests.Models;
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

    public async Task Test_()
    {
        var context = _couchbaseFixture.GetDbContext;
      
    }

    [Fact]
    public async Task Test_ComplexObject()
    {
        var context = _couchbaseFixture.GetDbContext;
        var user = new User
        {
            ID = 1,
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
        await context.SaveChangesAsync();
        
        context.Remove(airline);
        await context.SaveChangesAsync();
        
        var airline1 = await context.Airlines.FindAsync("airline", 11);
        Assert.Null(airline1);
    }
    
    [Fact]
    public async Task Test_UpdateAsync()
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
    public async Task Test_AddRange()
    {
        var airlines = new List<Airline>
        {
            new Airline
            {
                Type = "airline",
                Id = 11,
                Callsign = "MILE-AIR",
                Country = "United States",
                Icao = "MLA",
                Iata = "Q5",
                Name = "40-Mile Air"
            },
            new Airline
            {
                Type = "airline",
                Id = 11,
                Callsign = "MILE-AIR",
                Country = "United States",
                Icao = "MLA",
                Iata = "Q5",
                Name = "40-Mile Air"
            }
        };
    }
}