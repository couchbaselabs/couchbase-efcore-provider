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
        ctx.Update(new BloggingFixture.Tag { TagId = "general" });
        ctx.Update(new BloggingFixture.Tag { TagId = "classic" });
        ctx.Update(new BloggingFixture.Tag { TagId = "opinion" });
        ctx.Update(new BloggingFixture.Tag { TagId = "informative" });
        ctx.Update(new BloggingFixture.PostTag { PostTagId = 1, PostId = 1, TagId = "general" });
        ctx.Update(new BloggingFixture.PostTag { PostTagId = 2, PostId = 1, TagId = "informative" });
        ctx.Update(new BloggingFixture.PostTag { PostTagId = 3, PostId = 2, TagId = "classic" });
        ctx.Update(new BloggingFixture.PostTag { PostTagId = 4, PostId = 3, TagId = "opinion" });
        ctx.Update(new BloggingFixture.PostTag { PostTagId = 5, PostId = 4, TagId = "opinion" });
        ctx.Update(new BloggingFixture.PostTag { PostTagId = 6, PostId = 4, TagId = "informative" });
        await ctx.SaveChangesAsync();

        // Seed skip-navigation join table data for Posts 1 and 4 only.
        // Posts 2 and 3 intentionally have no DirectTags (exercises the empty-collection path).
        // Load with Include so Clear() can remove existing join entries (idempotent reseed).
        // Post 3 is reset to empty too: SkipNav_AddRelationship_WritesJoinDocumentWithCorrectKey
        // adds a tag to it, so without this reset its precondition would fail on the next run.
        var post1 = await ctx.Posts.Include(p => p.DirectTags).FirstAsync(p => p.PostId == 1);
        var post3 = await ctx.Posts.Include(p => p.DirectTags).FirstAsync(p => p.PostId == 3);
        var post4 = await ctx.Posts.Include(p => p.DirectTags).FirstAsync(p => p.PostId == 4);
        var tagGeneral     = await ctx.Set<BloggingFixture.Tag>().FindAsync("general");
        var tagInformative = await ctx.Set<BloggingFixture.Tag>().FindAsync("informative");
        var tagOpinion     = await ctx.Set<BloggingFixture.Tag>().FindAsync("opinion");
        post1.DirectTags.Clear();
        post1.DirectTags.Add(tagGeneral!);
        post1.DirectTags.Add(tagInformative!);
        post3.DirectTags.Clear();
        post4.DirectTags.Clear();
        post4.DirectTags.Add(tagOpinion!);
        post4.DirectTags.Add(tagInformative!);
        await ctx.SaveChangesAsync();

        // Warm-up: with RequestPlus this is already consistent, but as a belt-and-suspenders
        // guard against index lag, poll until the canonical seed for Post 1 is observable before
        // the test body runs.
        await WaitForSeedAsync();
    }

    // Poll the index a few times until Post 1's two DirectTags are visible. No-op in practice
    // when scan consistency is RequestPlus; protects the read tests if it is ever relaxed.
    // Throws if the seed never becomes visible so the failure is reported here — with an
    // actionable message — rather than as an opaque assertion failure inside a later test.
    private async Task WaitForSeedAsync()
    {
        const int maxAttempts = 10;
        const int delayMs = 100;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            // Dispose the context before delaying so we don't hold a connection open while waiting.
            int directTagCount;
            await using (var ctx = fixture.GetDbContext())
            {
                var post1 = await ctx.Posts.Include(p => p.DirectTags).FirstOrDefaultAsync(p => p.PostId == 1);
                directTagCount = post1?.DirectTags.Count ?? -1;
            }
            if (directTagCount == 2)
                return;
            await Task.Delay(delayMs);
        }

        throw new TimeoutException(
            $"EagerLoadingTests seed did not become visible after {maxAttempts} attempts " +
            $"({maxAttempts * delayMs}ms): Post 1 was expected to have 2 DirectTags. " +
            "The N1QL index may be lagging behind the seed writes, or the seed failed.");
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

    [Fact]
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

    [Fact]
    public async Task Include_Posts_WithFilter_MultipleMatches_ReturnsAllMatching()
    {
        // Regression guard for the projection-alias collision fix: the predicate must keep
        // MORE THAN ONE dependent row per principal (the original failing case returned a
        // single row per blog, which masked mis-aligned multi-row materialization).
        await using var context = fixture.GetDbContext();

        var blogs = await context.Blogs
            .Include(b => b.Posts.Where(p => p.Rating >= 4))
            .OrderBy(b => b.BlogId)
            .ToListAsync();

        // Blog 2 has posts rated 5, 4, 3 → ratings 5 and 4 match.
        var blog2 = blogs.First(b => b.BlogId == 2);
        Assert.Equal(2, blog2.Posts.Count);
        Assert.Equal([4, 5], blog2.Posts.Select(p => p.Rating).OrderBy(r => r));
        // The dependent's own FK column must be the post's, not the principal's leaking through.
        Assert.All(blog2.Posts, p => Assert.Equal(2, p.BlogId));
    }

    [Fact]
    public async Task Include_Posts_Unfiltered_DependentCollidingColumnsAreCorrect()
    {
        // Blog and Post both expose `rating` and `blogId`. This asserts the dependent's values
        // for those colliding columns — before the alias-uniquification fix the post read the
        // blog's `rating` (and `blogId`) instead of its own, even without a filter.
        await using var context = fixture.GetDbContext();

        var blogs = await context.Blogs
            .Include(b => b.Posts)
            .OrderBy(b => b.BlogId)
            .ToListAsync();

        var blog2 = blogs.First(b => b.BlogId == 2);   // blog2.Rating == 4
        var post2 = blog2.Posts.First(p => p.PostId == 2);

        // Post 2's own rating is 5 — distinct from blog2's rating (4), so a leak is detectable.
        Assert.Equal(5, post2.Rating);
        Assert.Equal(2, post2.BlogId);
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

    // -----------------------------------------------------------------------
    // 9 — Explicit join-entity many-to-many (Post → PostTag → Tag)
    // Verifies that the existing FK-navigation Include chain works for the
    // explicit join-entity pattern before tackling true skip navigation.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Include_Post_Tags_ViaExplicitJoinEntity_PopulatesPostTags()
    {
        // Post 1 has two PostTags (general, informative).
        await using var context = fixture.GetDbContext();

        var post = await context.Posts
            .Include(p => p.Tags)
            .FirstAsync(p => p.PostId == 1);

        Assert.NotNull(post.Tags);
        Assert.Equal(2, post.Tags.Count);
        Assert.Contains(post.Tags, pt => pt.TagId == "general");
        Assert.Contains(post.Tags, pt => pt.TagId == "informative");
    }

    [Fact]
    public async Task Include_Post_Tags_ThenInclude_Tag_PopulatesTagEntities()
    {
        // Include the join entity then ThenInclude the Tag itself — both hops
        // must resolve correctly through two FK JOINs.
        await using var context = fixture.GetDbContext();

        var post = await context.Posts
            .Include(p => p.Tags)
                .ThenInclude(pt => pt.Tag)
            .FirstAsync(p => p.PostId == 1);

        Assert.NotNull(post.Tags);
        Assert.Equal(2, post.Tags.Count);
        Assert.All(post.Tags, pt => Assert.NotNull(pt.Tag));
        Assert.Contains(post.Tags, pt => pt.Tag.TagId == "general");
        Assert.Contains(post.Tags, pt => pt.Tag.TagId == "informative");
    }

    // -----------------------------------------------------------------------
    // 10 — True skip navigation many-to-many (Post.DirectTags ↔ Tag.DirectPosts)
    // Verifies that EF Core's HasMany/WithMany transparent join-table pattern
    // works with the Couchbase provider without any custom population code.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SkipNav_Include_DirectTags_PopulatesTags()
    {
        // Post 1 has two DirectTags (general, informative) via skip navigation.
        await using var context = fixture.GetDbContext();

        var post = await context.Posts
            .Include(p => p.DirectTags)
            .FirstAsync(p => p.PostId == 1);

        Assert.NotNull(post.DirectTags);
        Assert.Equal(2, post.DirectTags.Count);
        Assert.Contains(post.DirectTags, t => t.TagId == "general");
        Assert.Contains(post.DirectTags, t => t.TagId == "informative");
    }

    [Fact]
    public async Task SkipNav_Include_DirectTags_EmptyCollection_IsEmpty()
    {
        // Posts without DirectTags should have an empty collection, not null.
        await using var context = fixture.GetDbContext();

        var post = await context.Posts
            .Include(p => p.DirectTags)
            .FirstAsync(p => p.PostId == 2);

        Assert.NotNull(post.DirectTags);
        Assert.Empty(post.DirectTags);
    }

    [Fact]
    public async Task SkipNav_Include_DirectTags_MultiplePostsCorrectlyGrouped()
    {
        // Each post gets only its own tags — no cross-contamination.
        await using var context = fixture.GetDbContext();

        var posts = await context.Posts
            .Include(p => p.DirectTags)
            .OrderBy(p => p.PostId)
            .ToListAsync();

        var post1 = posts.First(p => p.PostId == 1);
        Assert.Equal(2, post1.DirectTags.Count);
        Assert.Contains(post1.DirectTags, t => t.TagId == "general");
        Assert.Contains(post1.DirectTags, t => t.TagId == "informative");

        var post4 = posts.First(p => p.PostId == 4);
        Assert.Equal(2, post4.DirectTags.Count);
        Assert.Contains(post4.DirectTags, t => t.TagId == "opinion");
        Assert.Contains(post4.DirectTags, t => t.TagId == "informative");
    }

    [Fact]
    public async Task SkipNav_Inverse_Include_DirectPosts_PopulatesPosts()
    {
        // Query from the Tag side — Tag.DirectPosts should be populated.
        await using var context = fixture.GetDbContext();

        var tag = await context.Set<BloggingFixture.Tag>()
            .Include(t => t.DirectPosts)
            .FirstAsync(t => t.TagId == "general");

        Assert.NotNull(tag.DirectPosts);
        Assert.Single(tag.DirectPosts);
        Assert.Equal(1, tag.DirectPosts.First().PostId);
    }

    [Fact]
    public async Task Include_Post_Tags_ThenInclude_Tag_MultiplePostsCorrectlyGrouped()
    {
        // Blog 2 has three posts with different tag sets — verify each post
        // gets only its own PostTags, not tags from other posts.
        await using var context = fixture.GetDbContext();

        var posts = await context.Posts
            .Include(p => p.Tags)
                .ThenInclude(pt => pt.Tag)
            .Where(p => p.BlogId == 2)
            .OrderBy(p => p.PostId)
            .ToListAsync();

        Assert.Equal(3, posts.Count);

        // Post 2: one tag (classic)
        var post2 = posts.First(p => p.PostId == 2);
        Assert.Single(post2.Tags);
        Assert.Equal("classic", post2.Tags[0].TagId);

        // Post 3: one tag (opinion)
        var post3 = posts.First(p => p.PostId == 3);
        Assert.Single(post3.Tags);
        Assert.Equal("opinion", post3.Tags[0].TagId);

        // Post 4: two tags (opinion, informative)
        var post4 = posts.First(p => p.PostId == 4);
        Assert.Equal(2, post4.Tags.Count);
        Assert.Contains(post4.Tags, pt => pt.TagId == "opinion");
        Assert.Contains(post4.Tags, pt => pt.TagId == "informative");
    }

    // -----------------------------------------------------------------------
    // GetPrimaryKey integration — verifies correct document key generation
    // for shared entity types (skip navigation join table) against the live DB.
    // These tests exercise both overloads indirectly via write/read round-trips.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SkipNav_AddRelationship_WritesJoinDocumentWithCorrectKey()
    {
        // Add a new skip-navigation relationship and read it back — confirms
        // the join document was written with a non-empty composite key.
        await using var context = fixture.GetDbContext();

        var post = await context.Posts
            .Include(p => p.DirectTags)
            .FirstAsync(p => p.PostId == 3);
        var tag = await context.Set<BloggingFixture.Tag>().FindAsync("classic");

        Assert.DoesNotContain(post.DirectTags, t => t.TagId == "classic");
        post.DirectTags.Add(tag!);
        await context.SaveChangesAsync();

        await using var verify = fixture.GetDbContext();
        var reloaded = await verify.Posts
            .Include(p => p.DirectTags)
            .FirstAsync(p => p.PostId == 3);

        Assert.Contains(reloaded.DirectTags, t => t.TagId == "classic");
    }

    [Fact]
    public async Task SkipNav_RemoveRelationship_DeletesJoinDocument()
    {
        // Remove a skip-navigation relationship — confirms the join document
        // is deleted (the relationship is no longer present after reload).
        await using var context = fixture.GetDbContext();

        var post = await context.Posts
            .Include(p => p.DirectTags)
            .FirstAsync(p => p.PostId == 1);

        Assert.Contains(post.DirectTags, t => t.TagId == "general");
        var tagToRemove = post.DirectTags.First(t => t.TagId == "general");
        post.DirectTags.Remove(tagToRemove);
        await context.SaveChangesAsync();

        await using var verify = fixture.GetDbContext();
        var reloaded = await verify.Posts
            .Include(p => p.DirectTags)
            .FirstAsync(p => p.PostId == 1);

        Assert.DoesNotContain(reloaded.DirectTags, t => t.TagId == "general");
    }

    [Fact]
    public async Task SkipNav_ReplaceAllRelationships_RoundTrips()
    {
        // Clear all DirectTags on a post and replace with a new set —
        // verifies both delete (Clear) and insert (Add) paths generate valid keys.
        await using var context = fixture.GetDbContext();

        var post = await context.Posts
            .Include(p => p.DirectTags)
            .FirstAsync(p => p.PostId == 4);
        var tagClassic = await context.Set<BloggingFixture.Tag>().FindAsync("classic");

        post.DirectTags.Clear();
        post.DirectTags.Add(tagClassic!);
        await context.SaveChangesAsync();

        await using var verify = fixture.GetDbContext();
        var reloaded = await verify.Posts
            .Include(p => p.DirectTags)
            .FirstAsync(p => p.PostId == 4);

        Assert.Single(reloaded.DirectTags);
        Assert.Equal("classic", reloaded.DirectTags.First().TagId);
    }
}
