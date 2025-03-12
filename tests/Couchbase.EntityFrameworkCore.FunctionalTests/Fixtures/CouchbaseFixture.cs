using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.FunctionalTests.Models;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using EFCore.NamingConventions;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using Xunit;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.Fixtures;

public class CouchbaseFixture : IAsyncLifetime
{
    private bool _disposed;
    private bool _created;

    public TravelSampleDbContext? TravelSampleContext { get; private set; }
    
    public BloggingContext BloggingContext { get; private set; }

    public Task InitializeAsync()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter(level => level >= LogLevel.Debug);
            builder.AddFile("Logs/myapp-{Date}-5.txt", LogLevel.Debug);
        });

        var options = new ClusterOptions()
            .WithConnectionString("http://127.0.0.1")
            .WithCredentials("Administrator", "password")
            .WithLogging(loggerFactory)
            .WithBuckets("default");

        TravelSampleContext = new TravelSampleDbContext(options);
        BloggingContext = new BloggingContext();
        return Task.CompletedTask;
    }

    public class TravelSampleDbContext : DbContext
    {
        private readonly ClusterOptions _clusterOptions;

        public TravelSampleDbContext(DbContextOptions<TravelSampleDbContext> options): base(options){}

        public TravelSampleDbContext()
        {
        }

        public  TravelSampleDbContext(ClusterOptions clusterOptions)
        {
            _clusterOptions = clusterOptions;
        }

        public DbSet<Airline> Airlines { get; set; }

        public DbSet<User> Users { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);
            optionsBuilder.UseCouchbase(_clusterOptions,
                couchbaseDbContextOptions =>
            {
                couchbaseDbContextOptions.Bucket = "travel-sample";
                couchbaseDbContextOptions.Scope = "inventory";
            });
            optionsBuilder.UseCamelCaseNamingConvention();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ConfigureToCouchbase(this);
            modelBuilder.Entity<Airline>().HasKey(x=>new {x.Type, x.Id});//composite key mapping
        }
    }

    public async Task DisposeAsync()
    {
       //await GetDbContext.DisposeAsync();
    }

    public async Task InitializeBloggingAsync()
    {
        if (_created)
        {
            var blogs = new List<Blog>
            {
                new Blog
                {
                    BlogId = 1, Url = @"https://devblogs.microsoft.com/dotnet", Rating = 5, OwnerId = 1,
                },
                new Blog { BlogId = 2, Url = @"https://mytravelblog.com/", Rating = 4, OwnerId = 3 }
            };

            var posts = new List<Post>
            {
                new Post
                {
                    PostId = 1,
                    BlogId = 1,
                    Title = "What's new",
                    Content = "Lorem ipsum dolor sit amet",
                    Rating = 5,
                    AuthorId = 1
                },
                new Post
                {
                    PostId = 2,
                    BlogId = 2,
                    Title = "Around the World in Eighty Days",
                    Content = "consectetur adipiscing elit",
                    Rating = 5,
                    AuthorId = 2
                },
                new Post
                {
                    PostId = 3,
                    BlogId = 2,
                    Title = "Glamping *is* the way",
                    Content = "sed do eiusmod tempor incididunt",
                    Rating = 4,
                    AuthorId = 3
                },
                new Post
                {
                    PostId = 4,
                    BlogId = 2,
                    Title = "Travel in the time of pandemic",
                    Content = "ut labore et dolore magna aliqua",
                    Rating = 3,
                    AuthorId = 3
                }
            };

            var persons = new List<Person>
            {
                new Person { PersonId = 1, Name = "Dotnet Blog Admin", PhotoId = 1 },
                new Person { PersonId = 2, Name = "Phileas Fogg", PhotoId = 2 },
                new Person { PersonId = 3, Name = "Jane Doe", PhotoId = 3 }
            };

            var personPhotos = new List<PersonPhoto>
            {
                new PersonPhoto { PersonPhotoId = 1, Caption = "SN", Photo = new byte[] { 0x00, 0x01 } },
                new PersonPhoto { PersonPhotoId = 2, Caption = "PF", Photo = new byte[] { 0x01, 0x02, 0x03 } },
                new PersonPhoto { PersonPhotoId = 3, Caption = "JD", Photo = new byte[] { 0x01, 0x01, 0x01 } }
            };

            var tags = new List<Tag>
            {
                new Tag { TagId = "general" },
                new Tag { TagId = "classic" },
                new Tag { TagId = "opinion" },
                new Tag { TagId = "informative" }
            };

            var postTags = new List<PostTag>
            {
                new PostTag { PostTagId = 1, PostId = 1, TagId = "general" },
                new PostTag { PostTagId = 2, PostId = 1, TagId = "informative" },
                new PostTag { PostTagId = 3, PostId = 2, TagId = "classic" },
                new PostTag { PostTagId = 4, PostId = 3, TagId = "opinion" },
                new PostTag { PostTagId = 5, PostId = 4, TagId = "opinion" },
                new PostTag { PostTagId = 6, PostId = 4, TagId = "informative" }
            };
            
            var context = TravelSampleContext;
            await context.AddRangeAsync(blogs);
            await context.AddRangeAsync(posts);
            await context.AddRangeAsync(persons);

            await context.AddRangeAsync(personPhotos);
            await context.AddRangeAsync(tags);
            await context.AddRangeAsync(postTags);

            _created = true;
        }
    }
}