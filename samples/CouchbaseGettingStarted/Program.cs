// See https://aka.ms/new-console-template for more information

using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;

using (var db = new BloggingContext())
{
    // Note: This sample requires the Couchbase database to be created before running.
    //The Bucket name is "Content", the scope is "Blogs" and the collections are "Post and "Blog"

    // Create
    Console.WriteLine("Inserting a new blog");
    var blog = new Blog
    {
        Url = "http://blogs.msdn.com/adonet",
        BlogId = Guid.NewGuid().ToString()
    };
    db.Add(blog);
    await db.SaveChangesAsync();

    // Read
    Console.WriteLine("Querying for a blog");
    blog = await db.Blogs
        .OrderBy(b => b.BlogId)
        .FirstAsync();

    Console.WriteLine(blog.Url);

    // Update
    Console.WriteLine("Updating the blog and adding a post");
    blog.Url = "https://devblogs.microsoft.com/dotnet";
    blog.Posts.Add(
        new Post
        {
            Title = "Hello World",
            Content = "I wrote an app using EF Core!",
            PostId = Guid.NewGuid().ToString()
        });
    await db.SaveChangesAsync();

    // Delete
    Console.WriteLine("Delete the blog");
    db.Remove(blog);
    await db.SaveChangesAsync();
}
