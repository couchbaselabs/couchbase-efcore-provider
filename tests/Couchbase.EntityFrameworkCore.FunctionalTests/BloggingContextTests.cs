// Portions Copyright .NET foundation
// Copyright 2025 Couchbase, Inc.
// This file is under an MIT license as granted under license from the .NET Foundation

using Couchbase.EntityFrameworkCore.FunctionalTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCore.FunctionalTests;

public class BloggingContextTests
{
    private readonly ITestOutputHelper _outputHelper;

    public BloggingContextTests(ITestOutputHelper output)
    {
        _outputHelper = output;
    }

    [Fact]
    public async Task Removing_Relationships_Async()
    {
        using (var context = new BloggingContext())
        {
            var blog = new Blog { Url = "http://blogs.msdn.com/visualstudio", BlogId = 2 };
            var post = new Post { Title = "Intro to EF Core", PostId = 4, BlogId = 2 };
            try
            {
                context.Update(blog);
                await context.SaveChangesAsync();

                context.Update(post);
                await context.SaveChangesAsync();

                blog = await context.Blogs.FirstAsync();
                var posts = await context.Posts.ToListAsync();
                blog.Posts = await context.Posts.Where(x => x.BlogId == blog.BlogId).ToListAsync();
                post = blog.Posts.First();
            }
            catch (Exception ex)
            {
                _outputHelper.WriteLine(ex.Message);
            }
            finally
            {

                blog.Posts.Remove(post);
                await context.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task Test_Adding_Related_Entity()
    {
        await using (var context = new BloggingContext())
        {
            context.Update(new Blog { BlogId = 10, Url = "http://example.com" });
            context.Update(new Post { BlogId = 10, PostId = 10 });
            await context.SaveChangesAsync();

            var blog = await context.Blogs.FirstAsync();
            blog.Posts = await context.Posts.Where(x => x.BlogId == blog.BlogId).ToListAsync();

            var post = new Post { Title = "Intro to EF Core", PostId = 11 };
            blog.Posts.Add(post);
            await context.SaveChangesAsync();

            blog.Posts.Remove(post);
            await context.SaveChangesAsync();
        }
    }
}
