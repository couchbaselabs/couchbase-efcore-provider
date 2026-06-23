using Couchbase.EntityFrameworkCode.IntegrationTests.Models;
using Microsoft.EntityFrameworkCore;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;

public class BloggingFixture : CouchbaseFixture<BloggingDbContext>
{
    public override string ScopeName { get; } = "blogs";
    public override BloggingDbContext GetDbContext()
    {
        return new BloggingDbContext(CreateDbContextOptions<BloggingDbContext>());
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await using var ctx = GetDbContext();
        await ctx.Database.EnsureCreatedAsync();
        await LoadDataAsync();
    }

    public override async Task LoadDataAsync()
    {
        await using var dbContext = GetDbContext();
        var blogs = new List<Blog>
            {
                new Blog
                {
                    BlogId = 1, Url = @"https://devblogs.microsoft.com/dotnet",
                    Rating = 5, OwnerId = 1,
                },
                new Blog
                {
                    BlogId = 2, Url = @"https://mytravelblog.com/", Rating = 4,
                    OwnerId = 3
                }
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
                new Person
                    { PersonId = 1, Name = "Dotnet Blog Admin", PhotoId = 1 },
                new Person { PersonId = 2, Name = "Phileas Fogg", PhotoId = 2 },
                new Person { PersonId = 3, Name = "Jane Doe", PhotoId = 3 }
            };

            var personPhotos = new List<PersonPhoto>
            {
                new PersonPhoto
                {
                    PersonPhotoId = 1, Caption = "SN",
                    Photo = new byte[] { 0x00, 0x01 }
                },
                new PersonPhoto
                {
                    PersonPhotoId = 2, Caption = "PF",
                    Photo = new byte[] { 0x01, 0x02, 0x03 }
                },
                new PersonPhoto
                {
                    PersonPhotoId = 3, Caption = "JD",
                    Photo = new byte[] { 0x01, 0x01, 0x01 }
                },
                new PersonPhoto
                {
                    PersonPhotoId = 4, Caption = "SS",
                    Photo = new byte[] { 0x02, 0x02 }
                },
                new PersonPhoto
                {
                    PersonPhotoId = 5, Caption = "TT",
                    Photo = new byte[] { 0x03, 0x03 }
                }
            };

            var districts = new List<District>
            {
                new District { DistrictId = 1, Name = "Metro District" }
            };

            var schools = new List<School>
            {
                new School { SchoolId = 1, Name = "Couchbase University", DistrictId = 1 }
            };

            // Student and Teacher are derived types (TPH) stored in the "person" collection.
            var students = new List<Student>
            {
                new Student
                {
                    PersonId = 4, Name = "Sam Student", PhotoId = 4, SchoolId = 1,
                    Address = new StudentAddress { Street = "1 Database Way", City = "Mountain View" },
                    Contacts =
                    [
                        new StudentContact { Id = 1, Kind = "email", Value = "sam@university.edu" },
                        new StudentContact { Id = 2, Kind = "phone", Value = "555-0100" }
                    ]
                }
            };

            var teachers = new List<Teacher>
            {
                new Teacher
                {
                    PersonId = 5, Name = "Tina Teacher", PhotoId = 5, Subject = "Databases"
                }
            };

            var enrollments = new List<Enrollment>
            {
                new Enrollment { EnrollmentId = 1, StudentId = 4, Title = "Distributed Systems" },
                new Enrollment { EnrollmentId = 2, StudentId = 4, Title = "Query Optimization" }
            };

            // Abstract-base TPH hierarchy: seed concrete derived types only.
            var animals = new List<Animal>
            {
                new Dog { AnimalId = 1, Name = "Rex", Breed = "Beagle" },
                new Cat { AnimalId = 2, Name = "Whiskers", Indoor = true }
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
                new PostTag
                    { PostTagId = 2, PostId = 1, TagId = "informative" },
                new PostTag { PostTagId = 3, PostId = 2, TagId = "classic" },
                new PostTag { PostTagId = 4, PostId = 3, TagId = "opinion" },
                new PostTag { PostTagId = 5, PostId = 4, TagId = "opinion" },
                new PostTag { PostTagId = 6, PostId = 4, TagId = "informative" }
            };

            dbContext.UpdateRange(blogs);
            dbContext.UpdateRange(posts);
            dbContext.UpdateRange(persons);
            dbContext.UpdateRange(personPhotos);
            dbContext.UpdateRange(districts);
            dbContext.UpdateRange(schools);
            dbContext.UpdateRange(students);
            dbContext.UpdateRange(teachers);
            dbContext.UpdateRange(enrollments);
            dbContext.UpdateRange(animals);
            dbContext.UpdateRange(tags);
            dbContext.UpdateRange(postTags);

            await dbContext.SaveChangesAsync();
    }

    public class Blog
    {
        public int BlogId { get; set; }
        public string Url { get; set; }
        public int? Rating { get; set; }

        public List<Post> Posts { get; set; }

        public int OwnerId { get; set; }
        public Person Owner { get; set; }
    }

    public class Post
    {
        public int PostId { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public int Rating { get; set; }

        public int BlogId { get; set; }
        public Blog Blog { get; set; }

        public int AuthorId { get; set; }
        public Person Author { get; set; }

        public List<PostTag> Tags { get; set; }

        /// <summary>Skip navigation — Post ↔ Tag via EF Core's transparent join table.</summary>
        public ICollection<Tag> DirectTags { get; set; } = [];
    }

    public class Person
    {
        public int PersonId { get; set; }
        public string Name { get; set; }

        public List<Post> AuthoredPosts { get; set; }
        public List<Blog> OwnedBlogs { get; set; }

        public int? PhotoId { get; set; }
        public PersonPhoto Photo { get; set; }
    }

    public class PersonPhoto
    {
        public int PersonPhotoId { get; set; }
        public string Caption { get; set; }
        public byte[] Photo { get; set; }

        public Person Person { get; set; }
    }

    /// <summary>Derived type for TPH inheritance — shares the "person" collection
    /// with <see cref="Person"/>. Adds navigations (<see cref="School"/> reference,
    /// <see cref="Enrollments"/> collection) that only exist on the derived type.</summary>
    public class Student : Person
    {
        public int SchoolId { get; set; }
        public School School { get; set; }

        /// <summary>Collection navigation declared only on the derived type.</summary>
        public ICollection<Enrollment> Enrollments { get; set; } = [];

        /// <summary>Owned reference type embedded in the (shared) Student document.</summary>
        public StudentAddress Address { get; set; }

        /// <summary>Owned collection embedded in the (shared) Student document.</summary>
        public List<StudentContact> Contacts { get; set; } = [];
    }

    /// <summary>Owned (OwnsOne) type on the derived <see cref="Student"/>.</summary>
    public class StudentAddress
    {
        public string Street { get; set; }
        public string City { get; set; }
    }

    /// <summary>Owned (OwnsMany) item on the derived <see cref="Student"/>.
    /// Has an explicit <see cref="Id"/> so the owned collection uses a real key
    /// (an implicit shadow key cannot be set when seeding a disconnected graph).</summary>
    public class StudentContact
    {
        public int Id { get; set; }
        public string Kind { get; set; }
        public string Value { get; set; }
    }

    /// <summary>A second derived type, to verify discriminator filtering distinguishes
    /// between multiple derived types sharing the "person" collection.</summary>
    public class Teacher : Person
    {
        public string Subject { get; set; }
    }

    public class School
    {
        public int SchoolId { get; set; }
        public string Name { get; set; }

        /// <summary>Reference navigation used to verify ThenInclude off a derived navigation.</summary>
        public int? DistrictId { get; set; }
        public District District { get; set; }

        public ICollection<Student> Students { get; set; } = [];
    }

    public class District
    {
        public int DistrictId { get; set; }
        public string Name { get; set; }

        public ICollection<School> Schools { get; set; } = [];
    }

    public class Enrollment
    {
        public int EnrollmentId { get; set; }
        public string Title { get; set; }

        public int StudentId { get; set; }
        public Student Student { get; set; }
    }

    /// <summary>Abstract TPH base — never instantiated. A base-set query must
    /// materialise only the concrete derived types (<see cref="Dog"/>/<see cref="Cat"/>).</summary>
    public abstract class Animal
    {
        public int AnimalId { get; set; }
        public string Name { get; set; }
    }

    public class Dog : Animal
    {
        public string Breed { get; set; }
    }

    public class Cat : Animal
    {
        public bool Indoor { get; set; }
    }

    public class Tag
    {
        public string TagId { get; set; }

        public List<PostTag> Posts { get; set; }

        /// <summary>Inverse skip navigation for Post.DirectTags.</summary>
        public ICollection<Post> DirectPosts { get; set; } = [];
    }

    public class PostTag
    {
        public int PostTagId { get; set; }

        public int PostId { get; set; }
        public Post Post { get; set; }

        public string TagId { get; set; }
        public Tag Tag { get; set; }
    }
}