using Microsoft.EntityFrameworkCore;
using Couchbase;
using Couchbase.EntityFrameworkCore;
using Couchbase.Extensions.DependencyInjection;

public class BloggingContext : DbContext
{
    public DbSet<Blog> Blogs { get; set; }
    public DbSet<Post> Posts { get; set; }

    //The following configures the application to use a Couchbase cluster
    //on localhost with a Bucket named "universities" and a Scope named "contoso"
    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseCouchbase<INamedBucketProvider>(new ClusterOptions()
                .WithCredentials("Administrator", "password")
                .WithConnectionString("couchbase://localhost"),
            couchbaseDbContextOptions =>
            {
                couchbaseDbContextOptions.Bucket = "Blogging";
                couchbaseDbContextOptions.Scope = "MyBlog";
            });
}

public class Blog
{
    public int BlogId { get; set; }
    public string Url { get; set; }

    public List<Post> Posts { get; } = new();
}

public class Post
{
    public int PostId { get; set; }
    public string Title { get; set; }
    public string Content { get; set; }

    public int BlogId { get; set; }
    public Blog Blog { get; set; }
}