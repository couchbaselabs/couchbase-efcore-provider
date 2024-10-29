using ContosoUniversity;
using ContosoUniversity.Models;
using Couchbase.EntityFrameworkCore.FunctionalTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestModels.UpdatesModel;
using Xunit;
using Xunit.Abstractions;
using Person = Microsoft.EntityFrameworkCore.TestModels.UpdatesModel.Person;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.ContosoUniversityTests;

[Collection(ContosoTestingCollection.Name)]
public class StudentTests : IAsyncDisposable, IDisposable
{
    private readonly ContosoFixture _contosoFixture;
    private readonly ITestOutputHelper _outputHelper;

    public StudentTests(ContosoFixture contosoFixture, ITestOutputHelper outputHelper)
    {
        _contosoFixture = contosoFixture;
        _outputHelper = outputHelper;
    }

    //[InlineData(null, null, "Alonso", null)]
    //[Theory]
    public async Task Test_Get(string sortOrder,
        string currentFilter,
        string searchString,
        int? pageNumber)
    {
        if (searchString != null)
        {
            pageNumber = 1;
        }
        else
        {
            searchString = currentFilter;
        }
        var students = from s in _contosoFixture.DbContext.Students
            select s;
        if (!String.IsNullOrEmpty(searchString))
        {
            students = students.Where(s => s.LastName.Contains(searchString)
                                           || s.FirstMidName.Contains(searchString));
        }
        switch (sortOrder)
        {
            case "name_desc":
                students = students.OrderByDescending(s => s.LastName);
                break;
            case "Date":
                students = students.OrderBy(s => s.EnrollmentDate);
                break;
            case "date_desc":
                students = students.OrderByDescending(s => s.EnrollmentDate);
                break;
            default:
                students = students.OrderBy(s => s.LastName);
                break;
        }

        int pageSize = 3;
        var paginatedList =
            await PaginatedList<Student>.CreateAsync(students.AsNoTracking(), pageNumber ?? 1, pageSize);
        
        Assert.Equal(8, paginatedList.Count);
    }

    [Fact]
    public async Task Test_PersonStudent_AddRange()
    {
        var context = _contosoFixture.DbContext;
        context.Students.AddRange(_students);

        var count = await context.SaveChangesAsync();
        Assert.Equal(8, count);
    }

    [Fact]
    public async Task Test_PersonStudent_RemoveRange()
    {
        var context = _contosoFixture.DbContext;
        context.Students.AddRange(_students);

        var count = await context.SaveChangesAsync();
        Assert.Equal(8, count);

        context.Students.RemoveRange(_students);
        var count1 = await context.SaveChangesAsync();

        Assert.Equal(8, count1);
        Assert.Equal(0, await context.Students.CountAsync());
    }
    
    [Fact]
    public async Task Test_PersonStudent_UpdateRange()
    {
        var context = _contosoFixture.DbContext;
        context.Students.AddRange(_students);

        var count = await context.SaveChangesAsync();
      //  Assert.Equal(8, count);

        var results = await context.Students.ToListAsync();

        context.Students.RemoveRange(_students);
        var count1 = await context.SaveChangesAsync();

        Assert.Equal(8, count1);
    }

    #region Data
    
    Student[] _students =
    [
        new Student { FirstMidName = "Carson",   LastName = "Alexander",
            EnrollmentDate = DateTime.Parse("2010-09-01"), ID = 8 },
        new Student { FirstMidName = "Meredith", LastName = "Alonso",
            EnrollmentDate = DateTime.Parse("2012-09-01"), ID = 1 },
        new Student { FirstMidName = "Arturo",   LastName = "Anand",
            EnrollmentDate = DateTime.Parse("2013-09-01"), ID = 2 },
        new Student { FirstMidName = "Gytis",    LastName = "Barzdukas",
            EnrollmentDate = DateTime.Parse("2012-09-01"), ID = 3 },
        new Student { FirstMidName = "Yan",      LastName = "Li",
            EnrollmentDate = DateTime.Parse("2012-09-01"), ID = 4 },
        new Student { FirstMidName = "Peggy",    LastName = "Justice",
            EnrollmentDate = DateTime.Parse("2011-09-01"), ID = 5 },
        new Student { FirstMidName = "Laura",    LastName = "Norman",
            EnrollmentDate = DateTime.Parse("2013-09-01"), ID = 6 },
        new Student { FirstMidName = "Nino",     LastName = "Olivetto",
            EnrollmentDate = DateTime.Parse("2005-09-01"), ID = 7 }
    ];

    #endregion

    public async ValueTask DisposeAsync()
    {
        try
        {
            var context = _contosoFixture.DbContext;
            context.Students.RemoveRange(_students);
            await context.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _outputHelper.WriteLine(e.Message);
        }
    }

    public void Dispose()
    {
        DisposeAsync().GetAwaiter().GetResult();
    }
}