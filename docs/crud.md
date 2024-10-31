# Saving Data

## Two Approaches
While querying allows you to read data from the database, saving data means adding new entities to the database, removing entities, or modifying the properties of existing entities in some way. Entity Framework Core (EF Core) supports two fundamental approaches for saving data to the database.

## Approach 1: change tracking and SaveChanges
In many scenarios, your program needs to query some data from the database, perform some modification on it, and save those modifications back; this is sometimes referred to as a "unit of work". For example, let's assume that you have a set of Blogs, and you'd like to change the Url property of one of them.
> [NOTE] 
> The reasons for calling DbContext.Dispose in this manner are not the same in a RDBMS backed EFCore Provider as they are with the EFCore Couchbase DB Provider. The Couchbase Provider caches and reuses socket connections the entirety of the applications lifetime, so calling Dispose is effectively a NOOP.

In EF, this is typically done as follows:
```
using (var context = new BloggingContext())
{
    var blog = context.Blogs.Single(b => b.Url == "http://example.com");
    blog.Url = "http://example.com/blog";
    context.SaveChanges();
}
```
The code above performs the following steps:

* It uses a regular LINQ query to load an entity from the database and adds them to  the internal change tracker
* The retrieved entities Url property is modified.
* Finally, DbContext.SaveChanges() is called and the entities modified properties are persisted to the database.

## Approach 2: Bulk update with ExecuteUpdate and ExecuteDelete

> [!NOTE] 
> ExecuteUpdate and ExecuteDelete are only minimally supported in the developer preview for experimental usage only.

SaveChanges is a powerful model for persisting changes to the database, but has some limitations. The main limitation is that each entity must be tracked and when SaveChanges is called, each modified or added entity will be persisted to the database. This can be inefficient; a more efficient way of doing this is via an UPDATE or DELETE command.

The following is an example of a bulk operation using ExecuteDelete:
```
context.Blogs.Where(b => b.Rating < 3).ExecuteDelete();
```
An example of ExecuteUpdate is as follows:
```
 await context.Airlines
    .Where(a => a.Id > 665 && a.Id < 668)
    .ExecuteUpdateAsync(setters => 
        setters.SetProperty(a => a.Country, "Lilliput"));
```

More information on the differences between SaveChanges and ExecuteDelete and ExecuteDelete can be found in the [main EF Core documentation](https://learn.microsoft.com/en-us/ef/core/saving/).

## Adding Data
Use the DbSet<TEntity>.Add method to add new instances of your entity classes. The data will be inserted into the database when you call DbContext.SaveChanges():

```
using (var context = new BloggingContext())
{
    var blog = new Blog { Url = "http://example.com" };
    context.Blogs.Add(blog);
    context.SaveChanges();
}
```

## Updating Data
EF automatically detects changes made to an existing entity that is tracked by the context. This includes entities that you load/query from the database, and entities that were previously added and saved to the database.

Simply modify the values assigned to properties and then call SaveChanges:
```
using (var context = new BloggingContext())
{
    var blog = context.Blogs.Single(b => b.Url == "http://example.com");
    blog.Url = "http://example.com/blog";
    context.SaveChanges();
}
```
## Deleting Data
Similar to adding and updating data, deleting means using DbSet<TEntity>.Remove to delete entity instances from the database:
```
using (var context = new BloggingContext())
{
    var blog = context.Blogs.Single(b => b.Url == "http://example.com/blog");
    context.Blogs.Remove(blog);
    context.SaveChanges();
}
```
If the entity exists in the database, it will be removed from the database, otherwise it will be removed from the current context when SaveChanges is called.

## Combining Operations in a single SaveChanges
You can combine multiple Add/Update/Remove operations into a single call to SaveChanges:
```
using (var context = new BloggingContext())
{
    // seeding database
    context.Blogs.Add(new Blog { Url = "http://example.com/blog" });
    context.Blogs.Add(new Blog { Url = "http://example.com/another_blog" });
    context.SaveChanges();
}

using (var context = new BloggingContext())
{
    // add
    context.Blogs.Add(new Blog { Url = "http://example.com/blog_one" });
    context.Blogs.Add(new Blog { Url = "http://example.com/blog_two" });

    // update
    var firstBlog = context.Blogs.First();
    firstBlog.Url = "";

    // remove
    var lastBlog = context.Blogs.OrderBy(e => e.BlogId).Last();
    context.Blogs.Remove(lastBlog);

    context.SaveChanges();
}
```

> [!NOTE] 
> EF Core Couchbase DB Provider developer preview 1 uses the [Couchbase Key/Value store](https://docs.couchbase.com/dotnet-sdk/current/howtos/kv-operations.html) and not SQL++ for CRUD operations. This may change in later releases.

## Saving Related Data
In addition to isolated entities, you can also make use of the relationships defined in your model.

### Adding a Graph of New Items
If you create several new related entities, adding one of them to the context will cause the others to be added too.

In the following example, the blog and three related posts are all inserted into the database. The posts are found and added, because they are reachable via the Blog.Posts navigation property.
```
using (var context = new BloggingContext())
{
    var blog = new Blog
    {
        BlogId = 1,
        Url = "http://blogs.msdn.com/dotnet",
        Posts =
        [
            new() { Title = "Intro to C#", PostId = 1 },
            new() { Title = "Intro to VB.NET", PostId = 2 },
            new() { Title = "Intro to F#", PostId = 3 }
        ]
    };

    await context.Blogs.AddAsync(blog);
}
```

> [!TIP] 
> If you attempt to delete the Blog after creating it, you will get a foriegn key constraint when you call SaveChanges again, unless you provide DeleteBehavior.Cascade to the entity during modeling.

## Adding a related entity
If you reference a new entity from the navigation property of an entity that is already tracked by the context, the entity will be discovered and inserted into the database.
```
using (var context = new BloggingContext())
{
    var blog = context.Blogs.First();
    blog.Posts = context.Posts.Where(x => x.BlogId == blog.BlogId).ToList();
    var post = new Post { Title = "Intro to EF Core", PostId = 4};

    blog.Posts.Add(post);
    context.SaveChanges();
}
```

> [!NOTE] 
> Eager fetching via Include and/or ThenInclude is not supported in EF Core Couchbase DB Provider developer preview 1.

## Changing relationships
If you change the navigation property of an entity, the corresponding changes will be made to the foreign key column in the database.
```
using (var context = new BloggingContext())
{
    var blog = new Blog { Url = "http://blogs.msdn.com/visualstudio", BlogId = 2};
    var post = context.Posts.First();

    post.Blog = blog;
    await context.SaveChangesAsync();
}
```
## Removing relationships
You can remove a relationship by setting a reference navigation to null, or removing the related entity from a collection navigation.

Removing a relationship can have side effects on the dependent entity, according to the [cascade delete](https://learn.microsoft.com/en-us/ef/core/saving/cascade-delete) behavior configured in the relationship.
```
using (var context = new BloggingContext())
{
    var blog = context.Blogs.First();
    blog.Posts = context.Posts.Where(x => x.BlogId == blog.BlogId).ToList();
    var post = blog.Posts.First();

    blog.Posts.Remove(post);
    await context.SaveChangesAsync();
}
```