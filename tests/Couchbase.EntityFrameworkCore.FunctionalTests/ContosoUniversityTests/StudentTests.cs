using ContosoUniversity;
using ContosoUniversity.Data;
using ContosoUniversity.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

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
        ContosoContext context = _contosoFixture.DbContext();
        try
        {
            if (searchString != null)
            {
                pageNumber = 1;
            }
            else
            {
                searchString = currentFilter;
            }

            var students = from s in context.Students
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
                await PaginatedList<Student>.CreateAsync(students.AsNoTracking(), pageNumber ?? 1,
                    pageSize);

            Assert.Equal(8, paginatedList.Count);
        }
        finally
        {
            if (context != null)
            {
                context.Dispose();
            }
        }
    }

    [Fact]
    public async Task Test_PersonStudent_AddRange()
    {
        var context = _contosoFixture.DbContext();
        try
        {
            var students = DbInitializer.someStudents();
            await CleanUp(students);

            context.Students.AddRange(students);
            var count = await context.SaveChangesAsync();
            Assert.Equal(8, count);

            context.Students.RemoveRange(students);
            var count1 = await context.SaveChangesAsync();
            Assert.Equal(8, count1);

            Assert.Equal(0, await context.Students.CountAsync());
        }
        finally
        {
            if (context != null)
            {
                context.Dispose();
            }
        }
    }

    [Fact]
    public async Task Test_PersonStudent_RemoveRange()
    {
        var context = _contosoFixture.DbContext();
        try
        {
            var students = DbInitializer.someStudents();
            await CleanUp(students);

            context.Students.AddRange(students);
            var count = await context.SaveChangesAsync();
            Assert.Equal(8, count);

            context.Students.RemoveRange(students);
            var count1 = await context.SaveChangesAsync();
            Assert.Equal(8, count1);

            Assert.Equal(0, await context.Students.CountAsync());
        }
        finally
        {
            if (context != null)
            {
                context.Dispose();
            }
        }
    }

    [Fact]
    public async Task Test_PersonStudent_UpdateRange()
    {
        var context = _contosoFixture.DbContext();
        try
        {
            var students = DbInitializer.someStudents();
            await CleanUp(students);

            context.Students.AddRange(students);
            var count = await context.SaveChangesAsync();
            Assert.Equal(8, count);

            context.Students.UpdateRange(students);
            var count1 = await context.SaveChangesAsync();
            Assert.Equal(8, count1);

            context.Students.RemoveRange(students);
            var count2 = await context.SaveChangesAsync();
            Assert.Equal(8, count2);

            Assert.Equal(0, await context.Students.CountAsync());
        }
        finally
        {
            if (context != null)
            {
                context.Dispose();
            }
        }
    }
    
    public async ValueTask DisposeAsync()
    {
    }

    public void Dispose()
    {
        DisposeAsync().GetAwaiter().GetResult();
    }

    public async ValueTask CleanUp(Student[] students)
    {
        var context = _contosoFixture.DbContext();
        try
        {
            context.Students.RemoveRange(students);
            await context.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _contosoFixture.logger.LogDebug(e.Message);
        }
        finally
        {
            if (context != null)
            {
                context.Dispose();
            }
        }
    }
}