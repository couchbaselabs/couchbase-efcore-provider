using System.Text.Json;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Couchbase.EntityFrameworkCore.Infrastructure;

public class CouchbaseDbContextOptionsBuilder : ICouchbaseDbContextOptionsBuilder
{
    public CouchbaseDbContextOptionsBuilder(DbContextOptionsBuilder dbContextOptionsBuilder, string connectionString)
    {
        OptionsBuilder = dbContextOptionsBuilder;
        ClusterOptions = new ClusterOptions().WithConnectionString(connectionString);
    }

    public CouchbaseDbContextOptionsBuilder(DbContextOptionsBuilder dbContextOptionsBuilder, ClusterOptions clusterOptions)
    {
        OptionsBuilder = dbContextOptionsBuilder;
        ClusterOptions = clusterOptions;
    }

    //TODO temp
    public string ConnectionString => ClusterOptions.ConnectionString! + $"?bucket={Bucket}";

    public DbContextOptionsBuilder OptionsBuilder { get; }

    public ClusterOptions ClusterOptions { get; }

    // Assigned during configuration (the couchbaseDbContextOptions action), not at construction;
    // the provider validates they are set before use.
    public string Bucket { get; set; } = null!;

    public string Scope { get; set; } = null!;

    public bool AutoCreateScopes { get; set; }

    public bool AutoCreateIndexes { get; set; }

    public JsonNamingPolicy? FieldNamingPolicy { get; set; } = JsonNamingPolicy.CamelCase;

    public JsonSerializerOptions? SerializerOptions { get; set; }

    public QueryScanConsistency ScanConsistency { get; set; } = QueryScanConsistency.NotBounded;

    public object? ServiceKey { get; set; }

    private IServiceProvider? _applicationServiceProvider;

    /// <summary>
    /// The application's service provider, captured by <c>AddCouchbase&lt;TContext&gt;</c> so the
    /// provider can resolve an application-registered shared cluster (see <see cref="ServiceKey"/>).
    /// Null when the context is configured outside DI (plain <c>UseCouchbase</c>), in which case the
    /// provider owns its own cluster.
    /// </summary>
    /// <remarks>
    /// Setting this eagerly captures the container's stable identity (<see cref="ApplicationContainerIdentity"/>)
    /// while the provider is alive, because the captured provider may be a scope that is later
    /// disposed — and the service-provider cache key must not resolve services from a disposed
    /// provider during later equality checks.
    /// </remarks>
    public IServiceProvider? ApplicationServiceProvider
    {
        get => _applicationServiceProvider;
        set
        {
            _applicationServiceProvider = value;
            ApplicationContainerIdentity = value?.GetService<IServiceScopeFactory>();
        }
    }

    /// <summary>
    /// A stable per-container identity (the application root's <see cref="IServiceScopeFactory"/>),
    /// captured when <see cref="ApplicationServiceProvider"/> is set. Used as part of the
    /// service-provider cache key so internal providers bound to one application container are not
    /// reused by another. Null when configured outside DI.
    /// </summary>
    public object? ApplicationContainerIdentity { get; private set; }

    DbContextOptionsBuilder ICouchbaseDbContextOptionsBuilder.OptionsBuilder => OptionsBuilder;
}

public interface ICouchbaseDbContextOptionsBuilder
{
    DbContextOptionsBuilder OptionsBuilder { get; }

    ClusterOptions ClusterOptions { get; }

    public string ConnectionString { get; }

    public string Bucket { get; set; }

    public string Scope { get; set; }

    /// <summary>
    /// Gets or sets whether to automatically create non-default scopes referenced by entity mappings
    /// when <see cref="Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade.EnsureCreatedAsync"/> is called.
    /// Defaults to false.
    /// </summary>
    /// <remarks>
    /// When false, collections mapped to non-default scopes will be skipped with a warning.
    /// When true, any scopes referenced in entity keyspace mappings will be created automatically.
    /// </remarks>
    public bool AutoCreateScopes { get; set; }

    /// <summary>
    /// Gets or sets whether to automatically create a primary index on every collection referenced
    /// by entity mappings when <see cref="Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade.EnsureCreatedAsync"/>
    /// is called. Defaults to false.
    /// </summary>
    /// <remarks>
    /// Couchbase's query service refuses to run any N1QL query — every LINQ query, `FromSqlRaw`/
    /// `FromSql`, and `ExecuteUpdate`/`ExecuteDelete` — against a collection with no index at all.
    /// When true, <c>EnsureCreatedAsync</c> issues <c>CREATE PRIMARY INDEX IF NOT EXISTS</c> for
    /// each collection it creates or already owns, and waits for the index to report online before
    /// returning. A primary index is enough to get started but scans the whole collection; this
    /// option does not create secondary indexes — those still need to be created manually for real
    /// query performance. Collections skipped by <see cref="AutoCreateScopes"/> being false are
    /// also skipped here, since there is no collection to index.
    /// </remarks>
    public bool AutoCreateIndexes { get; set; }

    /// <summary>
    /// Controls how CLR navigation names are converted to JSON field names when reading and
    /// writing OwnsMany embedded collections. Defaults to <see cref="JsonNamingPolicy.CamelCase"/>
    /// to match the Couchbase SDK's default serializer (<c>JsonSerializerDefaults.Web</c>).
    /// Set to <c>null</c> to use the CLR name verbatim (PascalCase), or supply a custom policy
    /// such as <see cref="JsonNamingPolicy.SnakeCaseLower"/>.
    /// </summary>
    public JsonNamingPolicy? FieldNamingPolicy { get; set; }

    /// <summary>
    /// <see cref="JsonSerializerOptions"/> used when deserializing scalar values inside
    /// OwnsMany embedded collections. Defaults to <c>null</c>, which causes the provider to
    /// use <c>JsonSerializerDefaults.Web</c> — the same defaults the Couchbase SDK applies.
    /// Supply a custom instance to match a non-default serializer configured on the SDK
    /// (e.g. custom converters, different enum handling, or a different naming policy).
    /// </summary>
    public JsonSerializerOptions? SerializerOptions { get; set; }

    /// <summary>
    /// The N1QL scan consistency applied to the N1QL queries the provider executes — LINQ
    /// queries, <c>FromSql</c> queries, and ADO.NET <see cref="System.Data.Common.DbCommand"/>
    /// queries (does not affect schema/DDL operations such as scope/collection creation).
    /// Defaults to <see cref="QueryScanConsistency.NotBounded"/> (the SDK default — fastest, but
    /// may read a not-yet-indexed mutation). Set to <see cref="QueryScanConsistency.RequestPlus"/>
    /// to make a query wait until the index reflects all prior mutations — i.e. read-after-write
    /// consistency — at the cost of higher latency.
    /// </summary>
    public QueryScanConsistency ScanConsistency { get; set; }

    /// <summary>
    /// Optional key identifying which application-registered Couchbase cluster this context
    /// should use. When set, the provider resolves a shared <c>IClusterProvider</c> via keyed
    /// dependency injection (<c>services.AddKeyedCouchbase(serviceKey, ...)</c>) from the
    /// application's service provider — so a single <c>Cluster</c> per server is reused across
    /// contexts and buckets, per Couchbase guidance. Set a distinct key per physical Couchbase
    /// Server cluster when an application must talk to more than one.
    /// </summary>
    /// <remarks>
    /// When <c>null</c>, the provider uses the unkeyed application-registered cluster if one
    /// exists (<c>services.AddCouchbase(...)</c>), otherwise it falls back to registering and
    /// owning its own cluster from <see cref="ClusterOptions"/> (the original behavior).
    /// </remarks>
    public object? ServiceKey { get; set; }
}

/* ************************************************************
 *
 *    @author Couchbase <info@couchbase.com>
 *    @copyright 2025 Couchbase, Inc.
 *
 *    Licensed under the Apache License, Version 2.0 (the "License");
 *    you may not use this file except in compliance with the License.
 *    You may obtain a copy of the License at
 *
 *        http://www.apache.org/licenses/LICENSE-2.0
 *
 *    Unless required by applicable law or agreed to in writing, software
 *    distributed under the License is distributed on an "AS IS" BASIS,
 *    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *    See the License for the specific language governing permissions and
 *    limitations under the License.
 *
 * ************************************************************/
