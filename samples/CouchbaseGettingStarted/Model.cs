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

    //The following configures the application to use a Couchbase cluster
    //on localhost with a Bucket named "Content" and a Scope named "Blogs"
    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        using var loggingFactory = LoggerFactory.Create(builder => builder.AddConsole());
        options.UseCouchbase(
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
        modelBuilder.Entity<Blog>().ToCouchbaseCollection(this, "Blog");
        modelBuilder.Entity<Post>().ToCouchbaseCollection(this, "Post");
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