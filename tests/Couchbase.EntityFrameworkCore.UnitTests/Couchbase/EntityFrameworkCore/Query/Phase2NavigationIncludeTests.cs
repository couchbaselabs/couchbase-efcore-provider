using System.Linq.Expressions;
using Couchbase.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Moq;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Phase 2 verification: NavigationInclude record structure and context storage.
/// </summary>
public class Phase2NavigationIncludeTests
{
    // ---------------------------------------------------------------
    // 2.1 — NavigationInclude record
    // ---------------------------------------------------------------

    [Fact]
    public void NavigationInclude_StoresNavigationAndFilter()
    {
        var nav = MockNavigation("Posts");
        var filter = Expression.Lambda(Expression.Constant(true));
        var include = new NavigationInclude(nav, filter, []);

        Assert.Same(nav, include.Navigation);
        Assert.Same(filter, include.Filter);
        Assert.Empty(include.Children);
    }

    [Fact]
    public void NavigationInclude_NullFilter_IsAllowed()
    {
        var nav = MockNavigation("Tags");
        var include = new NavigationInclude(nav, null, []);

        Assert.Null(include.Filter);
    }

    [Fact]
    public void NavigationInclude_WithChildren_StoresChildIncludes()
    {
        var postNav = MockNavigation("Posts");
        var authorNav = MockNavigation("Author");

        var authorInclude = new NavigationInclude(authorNav, null, []);
        var postsInclude = new NavigationInclude(postNav, null, [authorInclude]);

        Assert.Single(postsInclude.Children);
        Assert.Same(authorNav, postsInclude.Children[0].Navigation);
    }

    [Fact]
    public void NavigationInclude_RecordEquality_SameInstancesAreEqual()
    {
        var nav = MockNavigation("Posts");
        var filter = Expression.Lambda(Expression.Constant(true));
        var children = new List<NavigationInclude>();

        // Records use reference equality for List<T> and LambdaExpression,
        // so all three fields must be the same reference.
        var a = new NavigationInclude(nav, filter, children);
        var b = new NavigationInclude(nav, filter, children);

        Assert.Equal(a, b);
    }

    [Fact]
    public void NavigationInclude_RecordEquality_DifferentFilterNotEqual()
    {
        var nav = MockNavigation("Posts");
        var children = new List<NavigationInclude>();

        var withFilter = new NavigationInclude(nav, Expression.Lambda(Expression.Constant(true)), children);
        var noFilter = new NavigationInclude(nav, null, children);

        Assert.NotEqual(withFilter, noFilter);
    }

    // ---------------------------------------------------------------
    // 2.2 — Multiple root includes (flat list)
    // ---------------------------------------------------------------

    [Fact]
    public void NavigationInclude_MultipleSiblings_CanBeStoredAsList()
    {
        var postsNav = MockNavigation("Posts");
        var tagsNav = MockNavigation("Tags");

        var includes = new List<NavigationInclude>
        {
            new(postsNav, null, []),
            new(tagsNav, null, [])
        };

        Assert.Equal(2, includes.Count);
        Assert.Equal("Posts", includes[0].Navigation.Name);
        Assert.Equal("Tags", includes[1].Navigation.Name);
    }

    // ---------------------------------------------------------------
    // 2.3 — NavigationInclude tree deduplication helpers
    // ---------------------------------------------------------------

    [Fact]
    public void NavigationInclude_Children_AllowsThenIncludeChaining()
    {
        // Blog.Posts → Post.Author (ThenInclude chain)
        var postsNav = MockNavigation("Posts");
        var authorNav = MockNavigation("Author");

        var authorInclude = new NavigationInclude(authorNav, null, []);
        var postsInclude = new NavigationInclude(postsNav, null, [authorInclude]);

        Assert.Equal("Posts", postsInclude.Navigation.Name);
        Assert.Equal("Author", postsInclude.Children[0].Navigation.Name);
    }

    [Fact]
    public void NavigationInclude_Children_AllowsMultipleThenIncludes()
    {
        // Blog.Posts → [Post.Author, Post.Tags] (two ThenIncludes on same nav)
        var postsNav = MockNavigation("Posts");
        var authorNav = MockNavigation("Author");
        var tagsNav = MockNavigation("Tags");

        var postsInclude = new NavigationInclude(postsNav, null,
        [
            new NavigationInclude(authorNav, null, []),
            new NavigationInclude(tagsNav, null, [])
        ]);

        Assert.Equal(2, postsInclude.Children.Count);
    }

    // ---------------------------------------------------------------
    // Helper
    // ---------------------------------------------------------------

    private static INavigation MockNavigation(string name)
    {
        var mock = new Mock<INavigation>();
        mock.Setup(n => n.Name).Returns(name);
        return mock.Object;
    }
}
