using Microsoft.EntityFrameworkCore.Migrations;

namespace Couchbase.EntityFrameworkCore.Migrations.Internal;

public class CouchbaseHistoryRepository : HistoryRepository
{
    public CouchbaseHistoryRepository(HistoryRepositoryDependencies dependencies) : base(dependencies)
    {
    }

    protected override string ExistsSql { get; }
    protected override bool InterpretExistsResult(object? value)
    {
        throw new NotImplementedException();
    }

    public override string GetCreateIfNotExistsScript()
    {
        throw new NotImplementedException();
    }

    public override string GetBeginIfNotExistsScript(string migrationId)
    {
        throw new NotImplementedException();
    }

    public override string GetBeginIfExistsScript(string migrationId)
    {
        throw new NotImplementedException();
    }

    public override string GetEndIfScript()
    {
        throw new NotImplementedException();
    }
}