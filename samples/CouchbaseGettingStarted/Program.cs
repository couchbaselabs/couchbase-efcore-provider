// See https://aka.ms/new-console-template for more information

using System;
using System.Linq;

using var db = new BloggingContext();

// Note: This sample requires the Couchbase database to be created before running.
//The Bucket name is "blogging", the scope is "myblog" and the collections are "post and "blog"

// Create
Console.WriteLine("Inserting a new blog");
db.Add(new Blog { Url = "http://blogs.msdn.com/adonet", BlogId = Guid.NewGuid().ToString()});
db.SaveChanges();

// Read
Console.WriteLine("Querying for a blog");
var blog = db.Blogs
    .OrderBy(b => b.BlogId)
    .First();

// Update
Console.WriteLine("Updating the blog and adding a post");
blog.Url = "https://devblogs.microsoft.com/dotnet";
blog.Posts.Add(
    new Post { Title = "Hello World", Content = "I wrote an app using EF Core!", PostId = Guid.NewGuid().ToString()});
db.SaveChanges();

// Delete
Console.WriteLine("Delete the blog");
db.Remove(blog);
db.SaveChanges();