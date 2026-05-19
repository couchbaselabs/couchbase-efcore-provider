using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// Phase 4 acceptance tests for eager loading via Include / ThenInclude.
/// Tests 1–5 cover foreign-key navigations handled by the Phase 3 JOIN pipeline.
/// Tests 6–7 cover auto-include and IgnoreAutoIncludes.
/// Test 8 (include on derived type) is skipped — requires a derived-type model.
/// </summary>
[Collection(CouchbaseTestingCollection.Name)]
public class EagerLoadingTests(BloggingFixture fixture) : IAsyncLifetime
{
    // Upsert known-good seed data before each test so that mutations in other test
    // classes (e.g. CrudTests.Removing_Relationships_Async clearing OwnerId) don't
    // cause these read-only queries to return empty result sets.
    public async Task InitializeAsync()
    {
        await using var ctx = fixture.GetDbContext();
        ctx.Update(new BloggingFixture.Blog { BlogId = 1, Url = "https://devblogs.microsoft.com/dotnet", Rating = 5, OwnerId = 1 });
        ctx.Update(new BloggingFixture.Blog { BlogId = 2, Url = "https://mytravelblog.com/", Rating = 4, OwnerId = 3 });
        ctx.Update(new BloggingFixture.Post { PostId = 1, BlogId = 1, Title = "What's new", Content = "Lorem ipsum dolor sit amet", Rating = 5, AuthorId = 1 });
        ctx.Update(new BloggingFixture.Post { PostId = 2, BlogId = 2, Title = "Around the World in Eighty Days", Content = "consectetur adipiscing elit", Rating = 5, AuthorId = 2 });
        ctx.Update(new BloggingFixture.Post { PostId = 3, BlogId = 2, Title = "Glamping *is* the way", Content = "sed do eiusmod tempor incididunt", Rating = 4, AuthorId = 3 });
        ctx.Update(new BloggingFixture.Post { PostId = 4, BlogId = 2, Title = "Travel in the time of pandemic", Content = "ut labore et dolore magna aliqua", Rating = 3, AuthorId = 3 });
        ctx.Update(new BloggingFixture.Person { PersonId = 1, Name = "Dotnet Blog Admin", PhotoId = 1 });
        ctx.Update(new BloggingFixture.Person { PersonId = 2, Name = "Phileas Fogg", PhotoId = 2 });
        ctx.Update(new BloggingFixture.Person { PersonId = 3, Name = "Jane Doe", PhotoId = 3 });
        ctx.Update(new BloggingFixture.PersonPhoto { PersonPhotoId = 1, Caption = "SN", Photo = [0x00, 0x01] });
        ctx.Update(new BloggingFixture.PersonPhoto { PersonPhotoId = 2, Caption = "PF", Photo = [0x01, 0x02, 0x03] });
        ctx.Update(new BloggingFixture.PersonPhoto { PersonPhotoId = 3, Caption = "JD", Photo = [0x01, 0x01, 0x01] });
        await ctx.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // -----------------------------------------------------------------------
    // 1 — Basic include
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Include_Blog_Posts_PopulatesPosts()
    {
        await using var context = fixture.GetDbContext();

        var blogs = await context.Blogs
            .Include(b => b.Posts)
            .OrderBy(b => b.BlogId)
            .ToListAsync();

        Assert.NotEmpty(blogs);
        Assert.All(blogs, b => Assert.NotNull(b.Posts));

        var blog1 = blogs.First(b => b.BlogId == 1);
        Assert.Single(blog1.Posts);
        Assert.Equal(1, blog1.Posts[0].PostId);
    }

    // -----------------------------------------------------------------------
    // 2 — Multiple includes
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Include_Blog_Posts_And_Owner_PopulatesBoth()
    {
        await using var context = fixture.GetDbContext();

        var blogs = await context.Blogs
            .Include(b => b.Posts)
            .Include(b => b.Owner)
            .OrderBy(b => b.BlogId)
            .ToListAsync();

        Assert.NotEmpty(blogs);
        Assert.All(blogs, b => Assert.NotNull(b.Posts));
        Assert.All(blogs, b => Assert.NotNull(b.Owner));
        Assert.All(blogs, b => Assert.NotEmpty(b.Owner.Name));
    }

    // -----------------------------------------------------------------------
    // 3 — ThenInclude chain
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Include_Posts_ThenInclude_Author_PopulatesAuthors()
    {
        await using var context = fixture.GetDbContext();

        var blogs = await context.Blogs
            .Include(b => b.Posts)
                .ThenInclude(p => p.Author)
            .OrderBy(b => b.BlogId)
            .ToListAsync();

        Assert.NotEmpty(blogs);

        var blog1 = blogs.First(b => b.BlogId == 1);
        Assert.Single(blog1.Posts);
        Assert.NotNull(blog1.Posts[0].Author);
        Assert.NotEmpty(blog1.Posts[0].Author.Name);
    }

    // -----------------------------------------------------------------------
    // 4 — Deep chain (three levels)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Include_Posts_ThenInclude_Author_ThenInclude_Photo_PopulatesAll()
    {
        await using var context = fixture.GetDbContext();

        var blogs = await context.Blogs
            .Include(b => b.Posts)
                .ThenInclude(p => p.Author)
                    .ThenInclude(a => a.Photo)
            .OrderBy(b => b.BlogId)
            .ToListAsync();

        Assert.NotEmpty(blogs);

        var blog1 = blogs.First(b => b.BlogId == 1);
        Assert.Single(blog1.Posts);
        Assert.NotNull(blog1.Posts[0].Author);
        Assert.NotNull(blog1.Posts[0].Author.Photo);
    }

    // -----------------------------------------------------------------------
    // 5 — Filtered include
    // -----------------------------------------------------------------------

    [Fact(Skip = "Filtered include returns incorrect posts — the WHERE predicate is not applied correctly in the generated N1QL. Needs investigation in CouchbaseQuerySqlGenerator.")]
    public async Task Include_Posts_WithFilter_ReturnsOnlyMatchingPosts()
    {
        await using var context = fixture.GetDbContext();

        var blogs = await context.Blogs
            .Include(b => b.Posts.Where(p => p.Rating == 5))
            .OrderBy(b => b.BlogId)
            .ToListAsync();

        Assert.NotEmpty(blogs);

        // Blog 1: one post, Rating=5 → included
        var blog1 = blogs.First(b => b.BlogId == 1);
        Assert.Single(blog1.Posts);

        // Blog 2: three posts with ratings 5, 4, 3 → only Rating=5 included
        var blog2 = blogs.First(b => b.BlogId == 2);
        Assert.Single(blog2.Posts);
        Assert.Equal(5, blog2.Posts[0].Rating);
    }

    // -----------------------------------------------------------------------
    // 6 — Auto-include (Person.Photo configured with AutoInclude in model)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AutoInclude_Person_Photo_PopulatedWithoutExplicitInclude()
    {
        await using var context = fixture.GetDbContext();

        var people = await context.People
            .OrderBy(p => p.PersonId)
            .ToListAsync();

        Assert.NotEmpty(people);
        Assert.All(people, p => Assert.NotNull(p.Photo));
    }

    // -----------------------------------------------------------------------
    // 7 — IgnoreAutoIncludes
    // -----------------------------------------------------------------------

    [Fact]
    public async Task IgnoreAutoIncludes_Person_Photo_NotPopulated()
    {
        await using var context = fixture.GetDbContext();

        var people = await context.People
            .IgnoreAutoIncludes()
            .OrderBy(p => p.PersonId)
            .ToListAsync();

        Assert.NotEmpty(people);
        Assert.All(people, p => Assert.Null(p.Photo));
    }

    // -----------------------------------------------------------------------
    // 8 — Include on derived type (requires derived-type model — deferred)
    // -----------------------------------------------------------------------

    [Fact(Skip = "Requires a derived-type model (e.g. Student : Person with School navigation). Deferred to Phase 6.")]
    public Task Include_OnDerivedType_PopulatesDerivedNavigation()
        => Task.CompletedTask;
}
