using System.Text.Json;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.Query;
using Microsoft.EntityFrameworkCore;

namespace Couchbase.EntityFrameworkCore.Infrastructure;

public class CouchbaseDbContextOptionsBuilder : ICouchbaseDbContextOptionsBuilder
{
    private readonly string _connectionString;

    public CouchbaseDbContextOptionsBuilder(DbContextOptionsBuilder dbContextOptionsBuilder, string connectionString)
    {
        _connectionString = connectionString;
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

    public string Bucket { get; set; }

    public string Scope { get; set; }

    public bool AutoCreateScopes { get; set; }

    public JsonNamingPolicy? FieldNamingPolicy { get; set; } = JsonNamingPolicy.CamelCase;

    public JsonSerializerOptions? SerializerOptions { get; set; }

    public QueryScanConsistency ScanConsistency { get; set; } = QueryScanConsistency.NotBounded;

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
