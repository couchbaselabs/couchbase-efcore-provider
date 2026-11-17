using System.Data.Common;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Couchbase.EntityFrameworkCore.SpecificationTests.Query;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;

/// <summary>
/// A <see cref="RelationalTestStore"/> for the suites that need one (TransactionTestBase,
/// FromSqlQueryTestBase, SqlQueryTestBase). It owns one <see cref="CouchbaseConnection"/> and, unless
/// <see cref="RelationalTestStore.UseConnectionString"/> is set, hands it to every context through
/// the options extension, so contexts and the store share connection and transaction exactly as
/// they would on a relational engine. Scope setup and cleaning are the same as
/// <see cref="CouchbaseTestStore"/>.
/// </summary>
public sealed class CouchbaseRelationalTestStore : RelationalTestStore
{
    private readonly CouchbaseTestStore _inner;
    private readonly ServiceProvider _connectionServices;

    private CouchbaseRelationalTestStore(string name, bool shared, (DbConnection Connection, ServiceProvider Services) connection)
        : base(name, shared, connection.Connection)
    {
        _inner = new CouchbaseTestStore(name, shared);
        _connectionServices = connection.Services;
    }

    public static CouchbaseRelationalTestStore GetOrCreate(string name) => new(name, true, CreateConnection(name));

    public static CouchbaseRelationalTestStore Create(string name) => new(name, false, CreateConnection(name));

    // The connection needs the provider's IBucketProvider and options; build them from the same
    // options a context for this store would use.
    private static (DbConnection, ServiceProvider) CreateConnection(string name)
    {
        var extension = new CouchbaseTestStore(name, shared: true)
            .AddProviderOptions(new DbContextOptionsBuilder())
            .Options.FindExtension<CouchbaseOptionsExtension>()!;
        var services = new ServiceCollection();
        extension.ApplyServices(services);
        var provider = services.BuildServiceProvider();
        return (new CouchbaseConnection(provider.GetRequiredService<IBucketProvider>(), extension.DbContextOptionsBuilder), provider);
    }

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

    public override Task CleanAsync(DbContext context) => _inner.CleanAsync(context);

    // The provider has no synchronous I/O; RelationalTestStore opens the shared connection from
    // synchronous test setup.
    public override void OpenConnection() => Connection.OpenAsync().GetAwaiter().GetResult();

    protected override string OpenDelimiter => "`";

    protected override string CloseDelimiter => "`";

    public override string NormalizeDelimitersInRawString(string sql)
        => CouchbaseRawSql.Normalize(sql, CouchbaseTestEnvironment.Bucket, Name);

    public override DbContextOptionsBuilder AddProviderOptions(DbContextOptionsBuilder builder)
    {
        _inner.AddProviderOptions(builder);
        if (!UseConnectionString)
        {
            var extension = builder.Options.FindExtension<CouchbaseOptionsExtension>()!;
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(
                (CouchbaseOptionsExtension)extension.WithConnection(Connection));
        }

        return builder;
    }

    /// <summary>Options for a context with its own connection (EF's "connection string" contexts).</summary>
    public DbContextOptionsBuilder AddProviderOptionsWithOwnConnection(DbContextOptionsBuilder builder)
        => _inner.AddProviderOptions(builder);

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _connectionServices.DisposeAsync();
    }
}

/// <summary>Store factory for the suites that cast the store to <see cref="RelationalTestStore"/>.</summary>
public class CouchbaseRelationalTestStoreFactory : SqlLoggingCouchbaseTestStoreFactory
{
    private CouchbaseRelationalTestStoreFactory(string scope) : base(scope)
    {
    }

    public new static CouchbaseRelationalTestStoreFactory ForStore(string storeName) => new(storeName);

    public override TestStore Create(string storeName) => CouchbaseRelationalTestStore.Create(storeName);

    public override TestStore GetOrCreate(string storeName) => CouchbaseRelationalTestStore.GetOrCreate(storeName);
}
