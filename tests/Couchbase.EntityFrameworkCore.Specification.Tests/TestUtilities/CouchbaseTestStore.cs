using Couchbase.Core.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;

/// <summary>
/// A test store backed by one Couchbase scope. EF's spec fixtures name each store after the
/// database they would create on a relational engine; here that name becomes the scope.
/// </summary>
public class CouchbaseTestStore : TestStore
{
    public CouchbaseTestStore(string name, bool shared) : base(name, shared)
    {
    }

    public static CouchbaseTestStore GetOrCreate(string name) => new(name, shared: true);

    public static CouchbaseTestStore Create(string name) => new(name, shared: false);

    protected override async Task InitializeAsync(
        Func<DbContext> createContext,
        Func<DbContext, Task>? seed,
        Func<DbContext, Task>? clean)
    {
        await using var context = createContext();
        if (clean != null)
        {
            await clean(context);
        }

        await CleanAsync(context);

        if (seed != null)
        {
            await seed(context);
        }
    }

    /// <summary>
    /// Leaves the store's scope empty but with its collections created. Deliberately does not use
    /// <c>Database.EnsureDeleted()</c>: the provider implements that by dropping the whole bucket,
    /// which would take every other scope in the shared test bucket with it.
    /// </summary>
    public override async Task CleanAsync(DbContext context)
    {
        // Creating a scope's collections and indexes is slow (minutes for the larger models), and
        // every suite usually runs in its own test process. So an existing scope is reused: its
        // documents are deleted and EnsureCreated adds whatever the model needs that is missing.
        // Set COUCHBASE_SPEC_RESET_SCOPES=1 to drop and recreate each scope once per run instead,
        // for example after a model change left collections or indexes that conflict.
        bool first;
        lock (InitializedScopes)
        {
            first = InitializedScopes.Add(Name);
        }

        if (first && ResetScopes)
        {
            await DropScopeAsync();
            await EnsureCreatedAsync(context);
            return;
        }

        // Documents first: EnsureCreated's index check fails on a collection that still holds
        // documents (a provider bug), so it must run against empty collections.
        await DeleteAllDocumentsAsync();
        await EnsureCreatedAsync(context);
    }

    // Management errors (e.g. creating a collection) carry the server's reason only in their
    // context, not the message; surface it so a failed fixture says why.
    private static async Task EnsureCreatedAsync(DbContext context)
    {
        try
        {
            await context.Database.EnsureCreatedAsync();
        }
        catch (CouchbaseException e) when (e.Context != null && string.IsNullOrEmpty(e.Message.Replace("Exception of type 'Couchbase.CouchbaseException' was thrown.", "")))
        {
            throw new InvalidOperationException($"EnsureCreated failed: {e.Context}", e);
        }
    }

    private static readonly HashSet<string> InitializedScopes = [];

    private static bool ResetScopes => Environment.GetEnvironmentVariable("COUCHBASE_SPEC_RESET_SCOPES") == "1";

    private async Task DeleteAllDocumentsAsync()
    {
        var cluster = await Cluster.ConnectAsync(
            CouchbaseTestEnvironment.ConnectionString,
            CouchbaseTestEnvironment.Username,
            CouchbaseTestEnvironment.Password);
        try
        {
            var bucket = await cluster.BucketAsync(CouchbaseTestEnvironment.Bucket);
            var scopeSpec = (await bucket.Collections.GetAllScopesAsync()).FirstOrDefault(s => s.Name == Name);
            if (scopeSpec == null) return;
            foreach (var collection in scopeSpec.Collections)
            {
                await cluster.QueryAsync<object>(
                    $"DELETE FROM `{CouchbaseTestEnvironment.Bucket}`.`{Name}`.`{collection.Name}`",
                    new global::Couchbase.Query.QueryOptions().ScanConsistency(global::Couchbase.Query.QueryScanConsistency.RequestPlus));
            }
        }
        finally
        {
            await cluster.DisposeAsync();
        }
    }

    private async Task DropScopeAsync()
    {
        var cluster = await Cluster.ConnectAsync(
            CouchbaseTestEnvironment.ConnectionString,
            CouchbaseTestEnvironment.Username,
            CouchbaseTestEnvironment.Password);
        try
        {
            var bucket = await cluster.BucketAsync(CouchbaseTestEnvironment.Bucket);
            await bucket.Collections.DropScopeAsync(Name);
        }
        catch (ScopeNotFoundException)
        {
            // Nothing to drop on a first run.
        }
        finally
        {
            await cluster.DisposeAsync();
        }
    }

    public override DbContextOptionsBuilder AddProviderOptions(DbContextOptionsBuilder builder)
        => builder.UseCouchbase(
            new ClusterOptions()
                .WithConnectionString(CouchbaseTestEnvironment.ConnectionString)
                .WithPasswordAuthentication(CouchbaseTestEnvironment.Username, CouchbaseTestEnvironment.Password),
            o =>
            {
                o.Bucket = CouchbaseTestEnvironment.Bucket;
                o.Scope = Name;
                o.AutoCreateScopes = true;
                // Long entity names produce index names the server can't create (a provider bug, recorded
                // separately); skip auto-indexing for that one suite's scope.
                o.AutoCreateIndexes = Name != "UpdateTest";
                // Spec tests seed and then immediately query through N1QL; wait for the index.
                o.ScanConsistency = global::Couchbase.Query.QueryScanConsistency.RequestPlus;
            }).ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
}
