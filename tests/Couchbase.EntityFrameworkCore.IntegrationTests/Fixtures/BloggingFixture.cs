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
                }
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

    public class Tag
    {
        public string TagId { get; set; }

        public List<PostTag> Posts { get; set; }
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