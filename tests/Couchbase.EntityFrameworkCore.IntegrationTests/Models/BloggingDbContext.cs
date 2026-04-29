using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Models;

public class BloggingDbContext(DbContextOptions<BloggingDbContext> options) : DbContext(options)
{
    public DbSet<BloggingFixture.Blog> Blogs { get; set; }
    public DbSet<BloggingFixture.Post> Posts { get; set; }
    public DbSet<BloggingFixture.Person> People { get; set; }
    public DbSet<BloggingFixture.PersonPhoto> PersonPhotos { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BloggingFixture.Blog>().ToCouchbaseCollection(this, "blog");
        modelBuilder.Entity<BloggingFixture.Blog>()
            .HasMany(b => b.Posts)
            .WithOne(p => p.Blog)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<BloggingFixture.PostTag>()
            .HasOne(pt => pt.Post)
            .WithMany(p => p.Tags)
            .HasForeignKey(pt => pt.PostId);

        modelBuilder.Entity<BloggingFixture.PostTag>()
            .HasOne(pt => pt.Tag)
            .WithMany(t => t.Posts)
            .HasForeignKey(pt => pt.TagId);

        modelBuilder.Entity<BloggingFixture.Blog>()
            .HasData(
                new BloggingFixture.Blog
                {
                    BlogId = 1, Url = @"https://devblogs.microsoft.com/dotnet",
                    Rating = 5, OwnerId = 1,
                },
                new BloggingFixture.Blog
                {
                    BlogId = 2, Url = @"https://mytravelblog.com/", Rating = 4,
                    OwnerId = 3
                });

        modelBuilder.Entity<BloggingFixture.Post>().ToCouchbaseCollection(this, "post");
        modelBuilder.Entity<BloggingFixture.Post>()
            .HasData(
                new BloggingFixture.Post
                {
                    PostId = 1,
                    BlogId = 1,
                    Title = "What's new",
                    Content = "Lorem ipsum dolor sit amet",
                    Rating = 5,
                    AuthorId = 1
                },
                new BloggingFixture.Post
                {
                    PostId = 2,
                    BlogId = 2,
                    Title = "Around the World in Eighty Days",
                    Content = "consectetur adipiscing elit",
                    Rating = 5,
                    AuthorId = 2
                },
                new BloggingFixture.Post
                {
                    PostId = 3,
                    BlogId = 2,
                    Title = "Glamping *is* the way",
                    Content = "sed do eiusmod tempor incididunt",
                    Rating = 4,
                    AuthorId = 3
                },
                new BloggingFixture.Post
                {
                    PostId = 4,
                    BlogId = 2,
                    Title = "Travel in the time of pandemic",
                    Content = "ut labore et dolore magna aliqua",
                    Rating = 3,
                    AuthorId = 3
                });

        modelBuilder.Entity<BloggingFixture.Person>().ToCouchbaseCollection(this, "person")
            .HasData(
                new BloggingFixture.Person
                    { PersonId = 1, Name = "Dotnet Blog Admin", PhotoId = 1 },
                new BloggingFixture.Person { PersonId = 2, Name = "Phileas Fogg", PhotoId = 2 },
                new BloggingFixture.Person { PersonId = 3, Name = "Jane Doe", PhotoId = 3 });

        modelBuilder.Entity<BloggingFixture.PersonPhoto>()
            .ToCouchbaseCollection(this, "personphoto")
            .HasData(
                new BloggingFixture.PersonPhoto
                {
                    PersonPhotoId = 1, Caption = "SN",
                    Photo = new byte[] { 0x00, 0x01 }
                },
                new BloggingFixture.PersonPhoto
                {
                    PersonPhotoId = 2, Caption = "PF",
                    Photo = new byte[] { 0x01, 0x02, 0x03 }
                },
                new BloggingFixture.PersonPhoto
                {
                    PersonPhotoId = 3, Caption = "JD",
                    Photo = new byte[] { 0x01, 0x01, 0x01 }
                });

        modelBuilder.Entity<BloggingFixture.Tag>()
            .HasData(
                new BloggingFixture.Tag { TagId = "general" },
                new BloggingFixture.Tag { TagId = "classic" },
                new BloggingFixture.Tag { TagId = "opinion" },
                new BloggingFixture.Tag { TagId = "informative" });

        modelBuilder.Entity<BloggingFixture.PostTag>()
            .HasData(
                new BloggingFixture.PostTag { PostTagId = 1, PostId = 1, TagId = "general" },
                new BloggingFixture.PostTag
                    { PostTagId = 2, PostId = 1, TagId = "informative" },
                new BloggingFixture.PostTag { PostTagId = 3, PostId = 2, TagId = "classic" },
                new BloggingFixture.PostTag { PostTagId = 4, PostId = 3, TagId = "opinion" },
                new BloggingFixture.PostTag { PostTagId = 5, PostId = 4, TagId = "opinion" },
                new BloggingFixture.PostTag
                    { PostTagId = 6, PostId = 4, TagId = "informative" });
        modelBuilder.ConfigureToCouchbase(this, true);
    }
}