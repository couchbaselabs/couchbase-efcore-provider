using System.Text.Json;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Storage.Internal;
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

    private string _dateTimeFormat = "yyyy-MM-ddTHH:mm:ss.FFFK";
    private string _goDateTimeFormat = DotNetToGoDateFormatConverter.Convert("yyyy-MM-ddTHH:mm:ss.FFFK");

    public string DateTimeFormat
    {
        get => _dateTimeFormat;
        set
        {
            // Convert eagerly so an unsupported token throws here, at configuration time, rather
            // than being deferred to whenever GoDateTimeFormat first happens to be read (typically
            // first query compilation) -- the XML doc on the interface promises the former.
            _goDateTimeFormat = DotNetToGoDateFormatConverter.Convert(value);
            _dateTimeFormat = value;
        }
    }

    public string GoDateTimeFormat => _goDateTimeFormat;

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
    /// The .NET custom <see cref="DateTime"/> format string this provider assumes when comparing
    /// against or generating <see cref="DateTime"/> string values in SQL++ — used for the LINQ
    /// <c>DateTime</c> function translators (<c>.Date</c>, <c>.Now</c>, <c>.UtcNow</c>, <c>.Today</c>)
    /// and for generating inline <see cref="DateTime"/> literals. Defaults to
    /// <c>"yyyy-MM-ddTHH:mm:ss.FFFK"</c> — millisecond-precision ISO-8601 with <c>Z</c> for UTC or
    /// a real offset otherwise — matching this provider's own default <see cref="DateTime"/>
    /// serialization.
    /// </summary>
    /// <remarks>
    /// N1QL has no native date type — dates are just JSON strings — so nothing stops a
    /// <see cref="DateTime"/> from being stored in a different format (date-only, different
    /// precision, or written by another system entirely). Set this to match your actual stored
    /// format if it differs from the default; a mismatch produces wrong comparisons, not an error.
    /// Only a bounded, ISO-8601-relevant subset of .NET custom format tokens is supported:
    /// <c>yyyy</c>, <c>MM</c>, <c>dd</c>, <c>HH</c>, <c>mm</c>, <c>ss</c>, <c>f</c>/<c>F</c>
    /// (repeated 1-7 times), <c>K</c>, non-letter literal separator characters (<c>-</c>,
    /// <c>:</c>, <c>.</c>, space), and the literal <c>T</c> (not a reserved .NET specifier, so it
    /// needs no quoting). A quoted literal string (<c>'...'</c>/<c>"..."</c>) or a
    /// backslash-escaped character (<c>\x</c>) can be used to include any other literal text,
    /// including letters — an unsupported bare token throws <see cref="ArgumentException"/>
    /// as soon as this is set, rather than producing a confusing SQL++ error later.
    /// </remarks>
    public string DateTimeFormat { get; set; }

    /// <summary>
    /// The Go reference-time layout string equivalent of <see cref="DateTimeFormat"/>, computed
    /// automatically. N1QL's date functions (<c>DATE_TRUNC_STR</c>, <c>NOW_LOCAL</c>,
    /// <c>NOW_UTC</c>) expect their <c>fmt</c> argument in this layout language, not .NET's.
    /// </summary>
    public string GoDateTimeFormat { get; }

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
