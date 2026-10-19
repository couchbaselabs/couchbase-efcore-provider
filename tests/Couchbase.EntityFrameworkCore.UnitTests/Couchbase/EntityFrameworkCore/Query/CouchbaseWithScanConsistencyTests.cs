using System.Linq.Expressions;
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

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Verifies the per-query <c>WithScanConsistency</c> operator: it is a pure execution-time option
/// (the generated SQL++ is unchanged), it is safe on non-EF-Core queryables, it chains with
/// <c>ConsistentWith</c> in either order, and the options the provider builds at execution time
/// apply the right scan consistency with the right precedence: per-query overrides the context-wide
/// default, and a <c>ConsistentWith</c> <see cref="MutationState"/> overrides both.
/// </summary>
public class CouchbaseWithScanConsistencyTests
{
    [Fact]
    public void WithScanConsistency_DoesNotAlterGeneratedSql()
    {
        using var ctx = CreateContext();
        var without = ctx.Posts.Where(p => p.Title == "x").ToQueryString();
        var with = ctx.Posts.Where(p => p.Title == "x").WithScanConsistency(QueryScanConsistency.RequestPlus).ToQueryString();

        Assert.Equal(without, with);
    }

    [Fact]
    public void WithScanConsistency_ComposedBeforeWhere_ThrowsTranslationException()
    {
        // Same documented "apply last" rule as ConsistentWith: a later operator that re-inspects the
        // shaper fails to translate rather than silently dropping the setting.
        using var ctx = CreateContext();

        Assert.Throws<InvalidOperationException>(
            () => ctx.Posts.WithScanConsistency(QueryScanConsistency.RequestPlus).Where(p => p.Title == "x").ToQueryString());
    }

    [Fact]
    public void WithScanConsistency_OnNonEntityQueryProvider_IsNoOp()
    {
        IQueryable<string> source = new List<string> { "a", "b", "c" }.AsQueryable();

        Assert.Same(source, source.WithScanConsistency(QueryScanConsistency.RequestPlus));
    }

    [Fact]
    public void WithScanConsistency_ChainedWithConsistentWith_TranslatesInEitherOrder()
    {
        using var ctx = CreateContext();
        var baseline = ctx.Posts.ToQueryString();

        Assert.Equal(baseline, ctx.Posts.WithScanConsistency(QueryScanConsistency.RequestPlus).ConsistentWith(new MutationState()).ToQueryString());
        Assert.Equal(baseline, ctx.Posts.ConsistentWith(new MutationState()).WithScanConsistency(QueryScanConsistency.RequestPlus).ToQueryString());
        Assert.Equal(
            baseline,
            ctx.Posts.WithScanConsistency(QueryScanConsistency.NotBounded).WithScanConsistency(QueryScanConsistency.RequestPlus).ToQueryString());
    }

    [Fact]
    public void Marker_Merge_KeepsBothOptions_AndLastCallWins()
    {
        var inner = Expression.Constant(1);

        var scanThenConsistent = CouchbaseQueryOptionsMarkerExpression.Merge(
            CouchbaseQueryOptionsMarkerExpression.Merge(inner, consistentWithParameterName: null, QueryScanConsistency.RequestPlus),
            "__p", scanConsistency: null);
        Assert.Same(inner, scanThenConsistent.Inner);
        Assert.Equal("__p", scanThenConsistent.ConsistentWithParameterName);
        Assert.Equal(QueryScanConsistency.RequestPlus, scanThenConsistent.ScanConsistency);

        var consistentThenScan = CouchbaseQueryOptionsMarkerExpression.Merge(
            CouchbaseQueryOptionsMarkerExpression.Merge(inner, "__p", scanConsistency: null),
            consistentWithParameterName: null, QueryScanConsistency.RequestPlus);
        Assert.Equal("__p", consistentThenScan.ConsistentWithParameterName);
        Assert.Equal(QueryScanConsistency.RequestPlus, consistentThenScan.ScanConsistency);

        var lastWins = CouchbaseQueryOptionsMarkerExpression.Merge(
            CouchbaseQueryOptionsMarkerExpression.Merge(inner, null, QueryScanConsistency.NotBounded),
            null, QueryScanConsistency.RequestPlus);
        Assert.Equal(QueryScanConsistency.RequestPlus, lastWins.ScanConsistency);
    }

    [Fact]
    public void GetParameters_PerQueryRequestPlus_OverridesNotBoundedContextDefault()
    {
        using var ctx = CreateContext(QueryScanConsistency.NotBounded);

        var (scanConsistency, scanVectors) = ReadScanConsistencyState(
            GetOptions(ctx, scanConsistency: QueryScanConsistency.RequestPlus));

        Assert.Equal("RequestPlus", scanConsistency);
        Assert.Null(scanVectors);
    }

    [Fact]
    public void GetParameters_PerQueryNotBounded_OverridesRequestPlusContextDefault()
    {
        using var ctx = CreateContext(QueryScanConsistency.RequestPlus);

        var (scanConsistency, _) = ReadScanConsistencyState(
            GetOptions(ctx, scanConsistency: QueryScanConsistency.NotBounded));

        Assert.Equal("NotBounded", scanConsistency);
    }

    [Fact]
    public void GetParameters_WithoutPerQuerySetting_UsesTheContextDefault()
    {
        using var ctx = CreateContext(QueryScanConsistency.RequestPlus);

        var (scanConsistency, _) = ReadScanConsistencyState(GetOptions(ctx, scanConsistency: null));

        Assert.Equal("RequestPlus", scanConsistency);
    }

    [Fact]
    public void GetParameters_ConsistentWithMutationState_WinsOverPerQueryScanConsistency()
    {
        using var ctx = CreateContext(QueryScanConsistency.NotBounded);
        var token = new MutationToken("test-bucket", 0, 1, 1);
        var mutationState = new MutationState().Add(new FakeMutationResult(token));

        var (scanConsistency, scanVectors) = ReadScanConsistencyState(
            GetOptions(ctx, QueryScanConsistency.RequestPlus, consistentWith: mutationState));

        Assert.Equal("AtPlus", scanConsistency);
        Assert.NotNull(scanVectors);
    }

    [Fact]
    public void GetParameters_ConsistentWithNull_LeavesThePerQueryScanConsistencyInForce()
    {
        using var ctx = CreateContext(QueryScanConsistency.NotBounded);

        var (scanConsistency, scanVectors) = ReadScanConsistencyState(
            GetOptions(ctx, QueryScanConsistency.RequestPlus, consistentWith: null, setConsistentWithParameter: true));

        Assert.Equal("RequestPlus", scanConsistency);
        Assert.Null(scanVectors);
    }

    private static QueryOptions GetOptions(
        QueryHintContext ctx,
        QueryScanConsistency? scanConsistency,
        MutationState? consistentWith = null,
        bool setConsistentWithParameter = false)
    {
        var relationalQueryContext = (RelationalQueryContext)ctx.GetService<IQueryContextFactory>().Create();

        const string parameterName = "__consistentWith_0";
        var useConsistentWith = consistentWith != null || setConsistentWithParameter;
        if (useConsistentWith)
        {
            relationalQueryContext.Parameters[parameterName] = consistentWith;
        }

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
            consistentWithParameterName: useConsistentWith ? parameterName : null,
            scanConsistency: scanConsistency);

        using var command = new CouchbaseCommand();
        return enumerable.GetParameters(command);
    }

    private sealed class FakeMutationResult(MutationToken token) : IMutationResult
    {
        public ulong Cas => 1;
        public MutationToken MutationToken { get; set; } = token;
    }

    // QueryOptions keeps its scan consistency in private fields (and its public form-value path
    // needs a statement, which GetParameters never sets), so read them via reflection, the same way
    // CouchbaseConsistentWithTests does.
    private static (string? ScanConsistency, object? ScanVectors) ReadScanConsistencyState(QueryOptions queryOptions)
    {
        var scanConsistencyField = typeof(QueryOptions).GetField("_scanConsistency", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var scanVectorsField = typeof(QueryOptions).GetField("_scanVectors", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (scanConsistencyField.GetValue(queryOptions)?.ToString(), scanVectorsField.GetValue(queryOptions));
    }

    private static QueryHintContext CreateContext(QueryScanConsistency contextDefault = QueryScanConsistency.NotBounded)
    {
        var clusterOptions = new ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");
        var builder = new DbContextOptionsBuilder<QueryHintContext>();
        builder.UseCouchbaseProvider(clusterOptions, o => o.ScanConsistency = contextDefault);
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
                b.ToCouchbaseCollection("bucket", "scope", "scanConsistencyPost");
                b.HasKey(p => p.Id);
            });
        }
    }
}
