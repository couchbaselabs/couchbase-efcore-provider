// Portions Copyright .NET foundation
// Copyright 2025 Couchbase, Inc.
// This file is under an MIT license as granted under license from the .NET Foundation

using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

[Collection(CouchbaseTestingCollection.Name)]
public class BloggingDbContextTests(
    BloggingFixture fixture,
    ITestOutputHelper output) : IAsyncLifetime
{
    // Upsert known-good seed data before each test so that mutations in other test
    // classes (e.g. CrudTests.Removing_Relationships_Async clearing OwnerId on Blog 2)
    // don't cause queries like Where(o => o.Owner.Name == "Jane Doe") to return empty.
    public async Task InitializeAsync()
    {
        await using var ctx = fixture.GetDbContext();
        ctx.Update(new BloggingFixture.Blog { BlogId = 1, Url = "https://devblogs.microsoft.com/dotnet", Rating = 5, OwnerId = 1 });
        ctx.Update(new BloggingFixture.Blog { BlogId = 2, Url = "https://mytravelblog.com/", Rating = 4, OwnerId = 3 });
        ctx.Update(new BloggingFixture.Person { PersonId = 1, Name = "Dotnet Blog Admin", PhotoId = 1 });
        ctx.Update(new BloggingFixture.Person { PersonId = 2, Name = "Phileas Fogg", PhotoId = 2 });
        ctx.Update(new BloggingFixture.Person { PersonId = 3, Name = "Jane Doe", PhotoId = 3 });
        await ctx.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Test_Filter_Nested_Entities()
    {
        await using var context = fixture.GetDbContext();
        var blogs = await context.Blogs.Where(o => o.Owner.Name == "Jane Doe")
            .ToListAsync();
        
        Assert.Single(blogs);
    }

    [Fact]
    public async Task Removing_Relationships_Async()
    {
        await using var context = fixture.GetDbContext();
        var blog = new BloggingFixture.Blog { Url = "http://blogs.msdn.com/visualstudio", BlogId = 10 };
        var post = new BloggingFixture.Post { Title = "Intro to EF Core", PostId = 1000, BlogId = 10 };
        try
        {
            context.Update(blog);
            context.Update(post);
            await context.SaveChangesAsync();

            blog = await context.Blogs.FirstAsync();
            var posts = await context.Posts.ToListAsync();
            blog.Posts = await context.Posts.Where(x => x.BlogId == blog.BlogId).ToListAsync();
            post = blog.Posts.First();
        }
        catch (Exception ex)
        {
            output.WriteLine(ex.Message);
        }
        finally
        {
            blog.Posts.Remove(post);
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Test_Adding_Related_Entity()
    {
        await using var context = fixture.GetDbContext();
        context.Update(new BloggingFixture.Blog { BlogId = 101, Url = "http://example.com" });
        context.Update(new BloggingFixture.Post { BlogId = 101, PostId = 10 });
        await context.SaveChangesAsync();

        var blog = await context.Blogs.FirstAsync();
        blog.Posts = await context.Posts.Where(x => x.BlogId == blog.BlogId).ToListAsync();

        var post = new BloggingFixture.Post { Title = "Intro to EF Core", PostId = 11 };
        blog.Posts.Add(post);
        await context.SaveChangesAsync();

        blog.Posts.Remove(post);
        await context.SaveChangesAsync();
    }
}
