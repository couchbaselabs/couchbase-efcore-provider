using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.Query;

public class NorthwindQueryCouchbaseFixture<TModelCustomizer> : NorthwindQueryRelationalFixture<TModelCustomizer>
    where TModelCustomizer : ITestModelCustomizer, new()
{
    protected override ITestStoreFactory TestStoreFactory => SqlLoggingCouchbaseTestStoreFactory.ForStore(StoreName);

    protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
    {
        base.OnModelCreating(modelBuilder, context);

        // Couchbase collection names can't contain spaces ("Order Details").
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var table = entityType.GetTableName();
            if (table != null && table.Contains(' '))
            {
                entityType.SetTableName(table.Replace(' ', '_'));
            }
        }

        modelBuilder.ConfigureToCouchbase(context);
    }
}

/// <summary>Relational spec bases cast the list logger factory to TestSqlLoggerFactory.</summary>
public class SqlLoggingCouchbaseTestStoreFactory : CouchbaseTestStoreFactory
{
    protected SqlLoggingCouchbaseTestStoreFactory(string scope) : base(scope)
    {
    }

    public new static SqlLoggingCouchbaseTestStoreFactory ForStore(string storeName) => new(storeName);

    public override ListLoggerFactory CreateListLoggerFactory(Func<string, bool> shouldLogCategory)
        => new TestSqlLoggerFactory(shouldLogCategory);
}
