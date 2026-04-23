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
    ITestOutputHelper output)
{
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
