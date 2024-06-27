using System;
using EFCore.MyCustom.Tests.Models;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace EFCore.MyCustom.Tests;

public class GettingStartedTests
{
    [Fact]
    public async Task Create_AddBlog()
    {
        using var db = new BloggingContext();
        db.Database.EnsureCreated();

        db.Add(new Blog { Url = "http://blogs.msdn.com/adonet" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Read_FirstBlog()
    {
        using var db = new BloggingContext();
        db.Database.EnsureCreated();

        var firstBlog = new Blog { Url = "http://blogs.msdn.com/adonet" };
        db.Add(firstBlog);
        db.Add(new Blog { Url = "https://www.billboard.com/" });
        db.Add(new Blog { Url = "https://www.wired.com/" });
        await db.SaveChangesAsync();

        var blog = db.Blogs
            .OrderBy(b => b.BlogId)
            .First();

        Assert.Equal(firstBlog.Url, blog.Url);
    }

    [Fact]
    public async Task Update_UpdateBlogAddPost() 
    {
        using var db = new BloggingContext();
        db.Database.EnsureCreated();

        db.Add(new Blog { Url = "http://blogs.msdn.com/adonet" });
        await db.SaveChangesAsync();

        var blog = db.Blogs
            .OrderBy(b => b.BlogId)
            .First();
        var updatedUrl = "https://devblogs.microsoft.com/dotnet";
        blog.Url = updatedUrl;
        blog.Posts.Add(
            new Post { Title = "Hello World", Content = "I wrote an app using EF Core!" });
        await db.SaveChangesAsync();

        blog = db.Blogs
            .OrderBy(b => b.BlogId)
            .First();

        Assert.Equal(updatedUrl, blog.Url);
        Assert.True(blog.Posts.Any());
    }

    [Fact]
    public async Task Delete_DeleteBlog()
    {
        using var db = new BloggingContext();
       // db.Database.EnsureCreated();

        var firstBlog = new Blog { Url = "http://blogs.msdn.com/adonet", BlogId = 1};
        db.Add(firstBlog);
        var secondBlog = "https://www.billboard.com/";
        db.Add(new Blog { Url = secondBlog , BlogId = 2});
        var thirdBlog = "https://www.wired.com/";
        db.Add(new Blog { Url = thirdBlog, BlogId = 3});
        await db.SaveChangesAsync();

        var blog = db.Blogs
            .OrderBy(b => b.BlogId)
            .First();

        Assert.Equal(firstBlog, blog);
        db.Remove(blog);
        await db.SaveChangesAsync();

        var blogs = db.Blogs.OrderBy(b => b.BlogId).ToList();
        Assert.Equal(2, db.Blogs.Count());
        Assert.Equal(secondBlog, blogs[0].Url);
        Assert.Equal(thirdBlog, blogs[1].Url);
    }
}
