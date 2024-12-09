# Getting started
The following are detailed steps for getting up and running with Couchbase.EntityFrameworkCore via a Console application. The Contoso University sample application is also a great way to become familier with the provider.

> [!NOTE]
> This is a clone of the [EFGettingStarted](https://learn.microsoft.com/en-us/ef/core/get-started/overview/first-app) example in the MS Docs.

## Prerequisites
* Download and Install [.NET 8](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
* Create a [Couchbase Capella](https://docs.couchbase.com/cloud/get-started/create-account.html) free  tier account.

## Create the Couchbase Capella database

>[NOTE]
> Checkout this [blog](https://jeffrymorris.net/2024/12/07/getting-started-with-ef-core-couchbase-db-provider/) for details on how to set up the Couchbase Capella database for this example.

After you have created your [Couchbase Capella](https://docs.couchbase.com/cloud/get-started/create-account.html) free tier, you must create a [Cluster](https://docs.couchbase.com/server/current/learn/clusters-and-availability/clusters-and-availability.html#clusters), a [Bucket](https://docs.couchbase.com/server/current/learn/buckets-memory-and-storage/buckets.html) name "Content", a [Scope](https://docs.couchbase.com/server/current/learn/data/scopes-and-collections.html) named "Blogs" and then [Collections](https://docs.couchbase.com/server/current/learn/data/scopes-and-collections.html) called "Blog" and "Post" for storing the documents. Note that names are *case-sensitive*!

## Create a Console application
* Create the .NET Console Application using [this tutorial](https://learn.microsoft.com/en-us/dotnet/core/tutorials/with-visual-studio-code?pivots=dotnet-8-0) or [Visual Studio](https://learn.microsoft.com/en-us/dotnet/core/tutorials/with-visual-studio?pivots=dotnet-8-0) or via the [Command Line](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-new)
```
mkdir CouchbaseGettingStarted
cd CouchbaseGettingStarted
dotnet new console
```
* Once you have created the console application add the dependency on Couchbase.EntityFrameworkCore:
```
dotnet install Couchbase.EntityFrameworkCore --version 1.0.0-dp1
```
* In the project directory, create Model.cs with the following code
```
using Microsoft.EntityFrameworkCore;
using Couchbase;
using Couchbase.EntityFrameworkCore;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public class BloggingContext : DbContext
{
    public DbSet<Blog> Blogs { get; set; }
    public DbSet<Post> Posts { get; set; }
    
    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        using var loggingFactory = LoggerFactory.Create(builder => builder.AddConsole());
        options.UseCouchbase<INamedBucketProvider>(
            new ClusterOptions()
                .WithCredentials("USERNAME", "PASSWORD")
                .WithConnectionString("couchbases://cb.xxxxxxxx.cloud.couchbase.com")
                .WithLogging(loggingFactory), 
            couchbaseDbContextOptions =>
            {
                couchbaseDbContextOptions.Bucket = "Content";
                couchbaseDbContextOptions.Scope = "Blogs";
            });
    }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Blog>().ToCouchbaseCollection("Blog");
        modelBuilder.Entity<Post>().ToCouchbaseCollection("Post");
    }
}

public class Blog
{
    public string BlogId { get; set; }
    public string Url { get; set; }
    public List<Post> Posts { get; } = new();
}

public class Post
{
    public string PostId { get; set; }
    public string Title { get; set; }
    public string Content { get; set; }
    public int BlogId { get; set; }
    public Blog Blog { get; set; }
}
```
## Create the database
Login into your Couchbase server and add the following Bucket _"Content"_:

![img_3.png](img_3.png)

Create the following Scope _"Blogs"_:

![img_4.png](img_4.png)

Create the following Collections for _"Blog"_ and _"Post"_:

![img_5.png](img_5.png)

> [!NOTE]
> You may also need to create a primary index on the Blogging bucket:
> CREATE PRIMARY INDEX ON `Content`

## Create, read, update & delete
* Open _Program.cs_ and replace the contents with the following code
```
using var db = new BloggingContext();

// Note: This sample requires the Couchbase database to be created before running.
// The Bucket name is "Content", the scope is "Blogs" and the collections are "Post and "Blog"
// Buckets, Scopes and Collections are case sensitive!

// Create
Console.WriteLine("Inserting a new blog");
var blog = new Blog
{
    Url = "http://blogs.msdn.com/adonet", 
    BlogId = Guid.NewGuid().ToString()
};
db.Add(blog);
db.SaveChanges();

// Read
Console.WriteLine("Querying for a blog");
blog = db.Blogs
    .OrderBy(b => b.BlogId)
    .First();

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
db.SaveChanges();

// Delete
Console.WriteLine("Delete the blog");
db.Remove(blog);
db.SaveChanges();
```

## Run the app
* _Run 'CouchbaseGettingStarted'_ in Rider IDE Or,
* _Debug > Start Without Debugging_ in VS Or,
* `dotnet run` in .NET CLI
```
/source/couchbase-dotnet-ef/samples/CouchbaseGettingStarted/bin/Debug/net8.0/CouchbaseGettingStarted
Inserting a new blog
Querying for a blog
Updating the blog and adding a post
Delete the blog

Process finished with exit code 0.

```
