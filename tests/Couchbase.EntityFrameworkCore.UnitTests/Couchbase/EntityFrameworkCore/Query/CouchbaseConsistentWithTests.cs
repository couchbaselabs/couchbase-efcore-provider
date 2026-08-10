using Couchbase.Core;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Query.Internal;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Couchbase.KeyValue;
using Couchbase.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Xunit;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Verifies <c>ConsistentWith</c>'s cross-provider safety, that it leaves generated SQL text
/// unaffected (it's a pure execution-time option, not a SQL-generation hint like
/// <c>UseIndex</c>/<c>UseHash</c>), and that <see cref="CouchbaseQueryEnumerable{T}.GetParameters"/>
/// correctly applies a <see cref="MutationState"/> found in the query context's parameters at
/// execution time.
/// </summary>
public class CouchbaseConsistentWithTests(ITestOutputHelper output)
{
    [Fact]
    public void ConsistentWith_DoesNotAlterGeneratedSql()
    {
        // .ConsistentWith(...) must be applied LAST (see its own doc remarks) -- composing further
        // operators afterward that need to re-inspect the shaper's shape (.Where/.OrderBy/.Select)
        // fails to translate, since the shaper is wrapped in a marker node at that point. Applied
        // last, it's purely an execution-time option and must leave the generated SQL untouched.
        using var ctx = CreateContext();
        var withoutHint = ctx.Posts.Where(p => p.Title == "x").ToQueryString();
        var withHint = ctx.Posts.Where(p => p.Title == "x").ConsistentWith(new MutationState()).ToQueryString();

        Assert.Equal(withoutHint, withHint);
    }

    [Fact]
    public void ConsistentWith_ComposedBeforeWhere_ThrowsTranslationException()
    {
        // Locks in the actual (not merely theorized) failure mode for the documented "apply last"
        // requirement: composing .Where(...) AFTER .ConsistentWith(...) throws a clear translation
        // exception rather than silently dropping the hint -- arguably the safer failure mode for a
        // correctness-affecting option, and important to pin down since it differs from
        // UseIndex/UseHash's own "silently ignored" fallback for a shape mismatch.
        using var ctx = CreateContext();

        Assert.Throws<InvalidOperationException>(
            () => ctx.Posts.ConsistentWith(new MutationState()).Where(p => p.Title == "x").ToQueryString());
    }

    [Fact]
    public void ConsistentWith_WithNull_DoesNotThrowOrAlterSql()
    {
        using var ctx = CreateContext();
        var withoutHint = ctx.Posts.ToQueryString();
        var withNull = ctx.Posts.ConsistentWith(null).ToQueryString();

        Assert.Equal(withoutHint, withNull);
    }

    [Fact]
    public void ConsistentWith_OnNonEntityQueryProvider_IsNoOp()
    {
        // Mirrors UseIndex/UseHash's own cross-provider-safety test: a genuine non-EF-Core
        // queryable (LINQ-to-Objects) has no translation pipeline to hook into, so the call must
        // be a silent no-op rather than throwing.
        IQueryable<string> source = new List<string> { "a", "b", "c" }.AsQueryable();
        var result = source.ConsistentWith(new MutationState());

        Assert.Same(source, result);
    }

    [Fact]
    public void GetParameters_WithConsistentWithParameterPresent_AppliesAtPlusConsistency()
    {
        using var ctx = CreateContext();
        var relationalQueryContext =
            (RelationalQueryContext)ctx.GetService<IQueryContextFactory>().Create();

        var token = new MutationToken("test-bucket", 0, 1, 1);
        var mutationState = new MutationState().Add(new FakeMutationResult(token));
        const string parameterName = "__consistentWith_0";
        relationalQueryContext.Parameters[parameterName] = mutationState;

        var enumerable = new CouchbaseQueryEnumerable<Post>(
            relationalQueryContext,
            _ => throw new NotSupportedException(),
            readerColumns: null,
            projectionAliases: null,
            ownedNavigationKeys: null,
            ownedNavigationAliases: null,
            shaper: (_, _, _, _) => throw new NotSupportedException(),
            contextType: typeof(QueryHintContext),
            standAloneStateManager: false,
            isTracking: false,
            detailedErrorsEnabled: false,
            threadSafetyChecksEnabled: false,
            bucketProvider: null!,
            couchbaseDbContextOptionsBuilder: ctx.GetService<ICouchbaseDbContextOptionsBuilder>(),
            consistentWithParameterName: parameterName);

        using var command = new CouchbaseCommand();
        var queryOptions = enumerable.GetParameters(command);
        var (scanConsistency, scanVectors) = ReadScanConsistencyState(queryOptions);

        output.WriteLine("scan_consistency: " + scanConsistency);
        Assert.Equal("AtPlus", scanConsistency);
        Assert.NotNull(scanVectors);
    }

    [Fact]
    public void GetParameters_WithNoConsistentWithParameter_LeavesDefaultScanConsistency()
    {
        using var ctx = CreateContext();
        var relationalQueryContext =
            (RelationalQueryContext)ctx.GetService<IQueryContextFactory>().Create();

        var enumerable = new CouchbaseQueryEnumerable<Post>(
            relationalQueryContext,
            _ => throw new NotSupportedException(),
            readerColumns: null,
            projectionAliases: null,
            ownedNavigationKeys: null,
            ownedNavigationAliases: null,
            shaper: (_, _, _, _) => throw new NotSupportedException(),
            contextType: typeof(QueryHintContext),
            standAloneStateManager: false,
            isTracking: false,
            detailedErrorsEnabled: false,
            threadSafetyChecksEnabled: false,
            bucketProvider: null!,
            couchbaseDbContextOptionsBuilder: ctx.GetService<ICouchbaseDbContextOptionsBuilder>(),
            consistentWithParameterName: null);

        using var command = new CouchbaseCommand();
        var queryOptions = enumerable.GetParameters(command);
        var (scanConsistency, scanVectors) = ReadScanConsistencyState(queryOptions);

        output.WriteLine("scan_consistency: " + scanConsistency);
        Assert.Equal("NotBounded", scanConsistency);
        Assert.Null(scanVectors);
    }

    private sealed class FakeMutationResult(MutationToken token) : IMutationResult
    {
        public ulong Cas => 1;
        public MutationToken MutationToken { get; set; } = token;
    }

    /// <summary>
    /// Reads <see cref="QueryOptions"/>'s private <c>_scanConsistency</c>/<c>_scanVectors</c>
    /// fields directly via reflection, rather than going through the public
    /// <c>GetFormValues()</c>/<c>CreateDto</c> path -- that path unconditionally throws
    /// ("A statement or prepared plan must be provided") unless a N1QL statement string was also
    /// supplied, which <see cref="CouchbaseQueryEnumerable{T}.GetParameters"/> never does (the
    /// statement is passed separately to <c>cluster.QueryAsync(statement, queryOptions)</c>, not
    /// stored on <see cref="QueryOptions"/> itself) -- so it's unusable here for a QueryOptions
    /// object built the same way production code builds one. <c>_scanConsistency</c>'s declared
    /// type (<c>QueryScanConsistencyInternal</c>) is itself an internal SDK enum, so the caller
    /// compares the returned value's <c>ToString()</c> rather than the type directly.
    /// </summary>
    private static (string? ScanConsistency, object? ScanVectors) ReadScanConsistencyState(QueryOptions queryOptions)
    {
        var scanConsistencyField = typeof(QueryOptions).GetField("_scanConsistency", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var scanVectorsField = typeof(QueryOptions).GetField("_scanVectors", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (scanConsistencyField.GetValue(queryOptions)?.ToString(), scanVectorsField.GetValue(queryOptions));
    }

    private static QueryHintContext CreateContext()
    {
        var clusterOptions = new ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");
        var builder = new DbContextOptionsBuilder<QueryHintContext>();
        builder.UseCouchbaseProvider(clusterOptions);
        return new QueryHintContext(builder.Options);
    }

    private class Post
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
    }

    private class QueryHintContext(DbContextOptions<QueryHintContext> options) : DbContext(options)
    {
        public DbSet<Post> Posts { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Post>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "consistentWithPost");
                b.HasKey(p => p.Id);
            });
        }
    }
}
