using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseDatabaseCreator :  RelationalDatabaseCreator
{
    public CouchbaseDatabaseCreator(RelationalDatabaseCreatorDependencies dependencies) : base(dependencies)
    {
    }

    public override bool Exists()
    {
        return true;
    }

    public override bool HasTables()
    {
        return true;
    }

    public override void Create()
    {
        base.Dependencies.CurrentContext.Context.SaveChanges();
    }

    public override void Delete()
    {
        throw new NotImplementedException();
    }
}