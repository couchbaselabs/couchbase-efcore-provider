using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Phase 1 verification: navigation metadata, keyspace mapping, and AutoInclude signal.
/// These tests exercise the EF Core model-building infrastructure that Phase 2+ (Include
/// translation) depends on.
/// </summary>
public class NavigationMetadataTests
{
    // ---------------------------------------------------------------
    // 1.1 — Relationship configuration
    // ---------------------------------------------------------------

    [Fact]
    public void HasMany_WithOne_NavigationsAreRegisteredOnBothSides()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.Entity<Blog>()
            .HasMany(b => b.Posts)
            .WithOne(p => p.Blog)
            .HasForeignKey(p => p.BlogId);

        var model = modelBuilder.FinalizeModel();

        var blogType = model.FindEntityType(typeof(Blog))!;
        var postType = model.FindEntityType(typeof(Post))!;

        // Blog has a collection navigation to Post
        var postsNav = blogType.FindNavigation(nameof(Blog.Posts));
        Assert.NotNull(postsNav);
        Assert.Equal(postType, postsNav.TargetEntityType);
        Assert.True(postsNav.IsCollection);

        // Post has a reference navigation back to Blog
        var blogNav = postType.FindNavigation(nameof(Post.Blog));
        Assert.NotNull(blogNav);
        Assert.Equal(blogType, blogNav.TargetEntityType);
        Assert.False(blogNav.IsCollection);
    }

    [Fact]
    public void HasForeignKey_ForeignKeyPropertyIsCorrect()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.Entity<Blog>()
            .HasMany(b => b.Posts)
            .WithOne(p => p.Blog)
            .HasForeignKey(p => p.BlogId);

        var model = modelBuilder.FinalizeModel();

        var postType = model.FindEntityType(typeof(Post))!;
        var blogNav = postType.FindNavigation(nameof(Post.Blog))!;
        var fk = blogNav.ForeignKey;

        Assert.NotNull(fk);
        Assert.Contains(fk.Properties, p => p.Name == nameof(Post.BlogId));
        Assert.Equal(model.FindEntityType(typeof(Blog)), fk.PrincipalEntityType);
    }

    [Fact]
    public void GetNavigations_ReturnsAllConfiguredNavigations()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.Entity<Blog>()
            .HasMany(b => b.Posts)
            .WithOne(p => p.Blog)
            .HasForeignKey(p => p.BlogId);

        var model = modelBuilder.FinalizeModel();

        var blogType = model.FindEntityType(typeof(Blog))!;
        var navigations = blogType.GetNavigations().ToList();

        Assert.Single(navigations);
        Assert.Equal(nameof(Blog.Posts), navigations[0].Name);
    }

    [Fact]
    public void ThenInclude_Chain_NavigationsAreReachableViaMetadata()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.Entity<Blog>()
            .HasMany(b => b.Posts)
            .WithOne(p => p.Blog)
            .HasForeignKey(p => p.BlogId);
        modelBuilder.Entity<Post>()
            .HasOne(p => p.Author)
            .WithMany()
            .HasForeignKey(p => p.AuthorId);

        var model = modelBuilder.FinalizeModel();

        // Walk the chain: Blog → Posts → Author
        var blogType = model.FindEntityType(typeof(Blog))!;
        var postsNav = blogType.FindNavigation(nameof(Blog.Posts))!;
        var authorNav = postsNav.TargetEntityType.FindNavigation(nameof(Post.Author));

        Assert.NotNull(authorNav);
        Assert.Equal(model.FindEntityType(typeof(Author)), authorNav!.TargetEntityType);
    }

    // ---------------------------------------------------------------
    // 1.2 — Keyspace mapping
    // ---------------------------------------------------------------

    [Fact]
    public void ToCouchbaseCollection_ExplicitOverload_SetsTableNameAsKeyspace()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.Entity<Blog>()
            .ToCouchbaseCollection("travel-sample", "inventory", "blog");

        var model = modelBuilder.FinalizeModel();

        var entityType = model.FindEntityType(typeof(Blog))!;
        var tableName = entityType.GetTableName();

        Assert.Equal("travel-sample.inventory.blog", tableName);
    }

    [Fact]
    public void ToCouchbaseCollection_ExplicitOverload_TableNameParseableAsCouchbaseKeyspace()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.Entity<Blog>()
            .ToCouchbaseCollection("my-bucket", "my-scope", "my-collection");

        var model = modelBuilder.FinalizeModel();

        var tableName = model.FindEntityType(typeof(Blog))!.GetTableName()!;
        var parsed = CouchbaseKeyspace.Parse(tableName);

        Assert.Equal("my-bucket", parsed.Bucket);
        Assert.Equal("my-scope", parsed.Scope);
        Assert.Equal("my-collection", parsed.Collection);
    }

    [Fact]
    public void ToCouchbaseCollection_MultipleEntities_EachGetsOwnKeyspace()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.Entity<Blog>()
            .ToCouchbaseCollection("bucket", "scope", "blogs");
        modelBuilder.Entity<Post>()
            .ToCouchbaseCollection("bucket", "scope", "posts");

        var model = modelBuilder.FinalizeModel();

        Assert.Equal("bucket.scope.blogs",  model.FindEntityType(typeof(Blog))!.GetTableName());
        Assert.Equal("bucket.scope.posts",  model.FindEntityType(typeof(Post))!.GetTableName());
    }

    [Fact]
    public void ToCouchbaseCollection_WithRelationship_KeyspacesAreIndependent()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.Entity<Blog>()
            .HasMany(b => b.Posts)
            .WithOne(p => p.Blog)
            .HasForeignKey(p => p.BlogId);
        modelBuilder.Entity<Blog>().ToCouchbaseCollection("b", "s", "blog");
        modelBuilder.Entity<Post>().ToCouchbaseCollection("b", "s", "post");

        var model = modelBuilder.FinalizeModel();

        Assert.Equal("b.s.blog", model.FindEntityType(typeof(Blog))!.GetTableName());
        Assert.Equal("b.s.post", model.FindEntityType(typeof(Post))!.GetTableName());
    }

    // ---------------------------------------------------------------
    // 1.3 — AutoInclude model configuration
    // ---------------------------------------------------------------

    [Fact]
    public void AutoInclude_Navigation_IsEagerLoadedIsTrue()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.Entity<Blog>()
            .HasMany(b => b.Posts)
            .WithOne(p => p.Blog)
            .HasForeignKey(p => p.BlogId);
        modelBuilder.Entity<Blog>()
            .Navigation(b => b.Posts)
            .AutoInclude();

        var model = modelBuilder.FinalizeModel();

        var nav = model.FindEntityType(typeof(Blog))!
            .FindNavigation(nameof(Blog.Posts))!;

        Assert.True(nav.IsEagerLoaded);
    }

    [Fact]
    public void AutoInclude_NotSet_IsEagerLoadedIsFalse()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.Entity<Blog>()
            .HasMany(b => b.Posts)
            .WithOne(p => p.Blog)
            .HasForeignKey(p => p.BlogId);

        var model = modelBuilder.FinalizeModel();

        var nav = model.FindEntityType(typeof(Blog))!
            .FindNavigation(nameof(Blog.Posts))!;

        Assert.False(nav.IsEagerLoaded);
    }

    [Fact]
    public void AutoInclude_Disabled_IsEagerLoadedIsFalse()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.Entity<Blog>()
            .HasMany(b => b.Posts)
            .WithOne(p => p.Blog)
            .HasForeignKey(p => p.BlogId);
        modelBuilder.Entity<Blog>()
            .Navigation(b => b.Posts)
            .AutoInclude(false);

        var model = modelBuilder.FinalizeModel();

        var nav = model.FindEntityType(typeof(Blog))!
            .FindNavigation(nameof(Blog.Posts))!;

        Assert.False(nav.IsEagerLoaded);
    }

    [Fact]
    public void AutoInclude_OnlyAppliesToConfiguredNavigation_OtherNavigationsUnaffected()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.Entity<Blog>()
            .HasMany(b => b.Posts)
            .WithOne(p => p.Blog)
            .HasForeignKey(p => p.BlogId);
        modelBuilder.Entity<Post>()
            .HasOne(p => p.Author)
            .WithMany()
            .HasForeignKey(p => p.AuthorId);

        // Only auto-include Posts
        modelBuilder.Entity<Blog>()
            .Navigation(b => b.Posts)
            .AutoInclude();

        var model = modelBuilder.FinalizeModel();

        var blogType  = model.FindEntityType(typeof(Blog))!;
        var postType  = model.FindEntityType(typeof(Post))!;

        Assert.True(blogType.FindNavigation(nameof(Blog.Posts))!.IsEagerLoaded);
        Assert.False(postType.FindNavigation(nameof(Post.Author))!.IsEagerLoaded);
    }

    // ---------------------------------------------------------------
    // Test model
    // ---------------------------------------------------------------

    private class Blog
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public List<Post> Posts { get; set; } = [];
    }

    private class Post
    {
        public int Id { get; set; }
        public int BlogId { get; set; }
        public Blog Blog { get; set; } = null!;
        public int AuthorId { get; set; }
        public Author Author { get; set; } = null!;
        public string Content { get; set; } = "";
    }

    private class Author
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }
}
