namespace Couchbase.EntityFrameworkCore.FunctionalTests.Fixtures;

public class BloggingFixture : CouchbaseFixtureBase
{
    protected override string ScopeName => "blogs";

    public override Task InitializeAsync()
    {
        DbContext = new BloggingContext();
        return base.InitializeAsync();
    }
    
    public BloggingContext CreateDbContext()
    {
        return new BloggingContext();
    }

    protected override async Task LoadDataAsync()
    {
        {
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

            await DbContext.AddRangeAsync(blogs);
            await DbContext.AddRangeAsync(posts);
            await DbContext.AddRangeAsync(persons);

            await DbContext.AddRangeAsync(personPhotos);
            await DbContext.AddRangeAsync(tags);
            await DbContext.AddRangeAsync(postTags);

            await DbContext.SaveChangesAsync();
        }
    }
}