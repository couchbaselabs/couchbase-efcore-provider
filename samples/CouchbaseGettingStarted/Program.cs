// See https://aka.ms/new-console-template for more information

using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;

using var db = new BloggingContext();

var blogId = Guid.NewGuid().ToString();

// Create
Console.WriteLine("Inserting a new blog");
db.Add(new Blog { Url = "http://blogs.msdn.com/adonet", BlogId = blogId });
await db.SaveChangesAsync();

// Read
Console.WriteLine("Querying for a blog");
var blog = await db.Blogs
    .OrderBy(b => b.BlogId)
    .FirstAsync();

// Update
Console.WriteLine("Updating the blog and adding a post");
blog.Url = "https://devblogs.microsoft.com/dotnet";
blog.Posts.Add(
    new Post { Title = "Hello World", Content = "I wrote an app using EF Core!", PostId = Guid.NewGuid().ToString()});
await db.SaveChangesAsync();

// Delete
Console.WriteLine("Delete the blog");
db.Remove(blog);
await db.SaveChangesAsync();
    await db.SaveChangesAsync();
