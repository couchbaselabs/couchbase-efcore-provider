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
        // throw new NotImplementedException();
    }

    public override bool HasTables()
    {
        return true;
        //throw new NotImplementedException();
    }

    public override void Create()
    {
        throw new NotImplementedException();
    }

    public override void Delete()
    {
        throw new NotImplementedException();
    }
}