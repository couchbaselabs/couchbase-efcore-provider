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
    public DbSet<BloggingFixture.School> Schools { get; set; }
    public DbSet<BloggingFixture.District> Districts { get; set; }
    public DbSet<BloggingFixture.Enrollment> Enrollments { get; set; }
    public DbSet<BloggingFixture.Animal> Animals { get; set; }

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

        modelBuilder.Entity<BloggingFixture.Person>()
            .Navigation(p => p.Photo)
            .AutoInclude();

        // TPH inheritance: Student and Teacher are derived types sharing the
        // "person" collection. Mapping them to the same collection as Person
        // signals TPH, so EF Core adds a "Discriminator" shadow property by default.
        modelBuilder.Entity<BloggingFixture.Student>(b =>
        {
            b.ToCouchbaseCollection(this, "person");

            b.HasOne(s => s.School)
                .WithMany(sc => sc.Students)
                .HasForeignKey(s => s.SchoolId);

            // Collection navigation declared only on the derived Student type.
            b.HasMany(s => s.Enrollments)
                .WithOne(e => e.Student)
                .HasForeignKey(e => e.StudentId);

            // Owned types on a derived type: embedded in the Student's document
            // within the shared "person" collection. Exercises the owned-nav write
            // branch of HydrateObjectFromEntity alongside the TPH discriminator.
            b.OwnsOne(s => s.Address);
            b.OwnsMany(s => s.Contacts);
        });
        // Student rows are seeded via LoadDataAsync / the per-test warm-up rather than
        // HasData: HasData on an owner with required owned navigations would also require
        // seeding the owned data through the owned builders, which adds no coverage here.

        modelBuilder.Entity<BloggingFixture.Teacher>()
            .ToCouchbaseCollection(this, "person");

        modelBuilder.Entity<BloggingFixture.Teacher>()
            .HasData(new BloggingFixture.Teacher
            {
                PersonId = 5, Name = "Tina Teacher", PhotoId = 5, Subject = "Databases"
            });

        // School → District reference, used to verify ThenInclude off a derived nav.
        modelBuilder.Entity<BloggingFixture.School>()
            .HasOne(sc => sc.District)
            .WithMany(d => d.Schools)
            .HasForeignKey(sc => sc.DistrictId);

        modelBuilder.Entity<BloggingFixture.School>()
            .ToCouchbaseCollection(this, "school")
            .HasData(new BloggingFixture.School { SchoolId = 1, Name = "Couchbase University", DistrictId = 1 });

        modelBuilder.Entity<BloggingFixture.District>()
            .ToCouchbaseCollection(this, "district")
            .HasData(new BloggingFixture.District { DistrictId = 1, Name = "Metro District" });

        modelBuilder.Entity<BloggingFixture.Enrollment>()
            .ToCouchbaseCollection(this, "enrollment")
            .HasData(
                new BloggingFixture.Enrollment { EnrollmentId = 1, StudentId = 4, Title = "Distributed Systems" },
                new BloggingFixture.Enrollment { EnrollmentId = 2, StudentId = 4, Title = "Query Optimization" });

        // Abstract TPH base: Dog and Cat share the "animal" collection. The abstract
        // Animal base is never instantiated; a base-set query must return concrete types.
        modelBuilder.Entity<BloggingFixture.Animal>().ToCouchbaseCollection(this, "animal");
        modelBuilder.Entity<BloggingFixture.Dog>()
            .HasData(new BloggingFixture.Dog { AnimalId = 1, Name = "Rex", Breed = "Beagle" });
        modelBuilder.Entity<BloggingFixture.Cat>()
            .HasData(new BloggingFixture.Cat { AnimalId = 2, Name = "Whiskers", Indoor = true });

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
                },
                new BloggingFixture.PersonPhoto
                {
                    PersonPhotoId = 4, Caption = "SS",
                    Photo = new byte[] { 0x02, 0x02 }
                },
                new BloggingFixture.PersonPhoto
                {
                    PersonPhotoId = 5, Caption = "TT",
                    Photo = new byte[] { 0x03, 0x03 }
                });

        modelBuilder.Entity<BloggingFixture.Tag>()
            .ToCouchbaseCollection(this, "tag")
            .HasData(
                new BloggingFixture.Tag { TagId = "general" },
                new BloggingFixture.Tag { TagId = "classic" },
                new BloggingFixture.Tag { TagId = "opinion" },
                new BloggingFixture.Tag { TagId = "informative" });

        // Skip navigation: Post ↔ Tag via EF Core's transparent join table.
        // ConfigureToCouchbase (called below) maps the hidden join entity to
        // bucket.scope.postdirecttag using EF Core's default naming.
        // "PostDirectTag" avoids collision with the explicit PostTag join entity.
        modelBuilder.Entity<BloggingFixture.Post>()
            .HasMany(p => p.DirectTags)
            .WithMany(t => t.DirectPosts)
            .UsingEntity("PostDirectTag");

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