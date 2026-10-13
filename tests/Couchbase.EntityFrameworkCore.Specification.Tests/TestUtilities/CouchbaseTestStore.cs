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
                // Spec tests seed and then immediately query through N1QL; wait for the index.
                o.ScanConsistency = global::Couchbase.Query.QueryScanConsistency.RequestPlus;
            }).ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
}
