using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Database = Microsoft.EntityFrameworkCore.Storage.Database;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseDatabaseWrapper : Database
{
    public CouchbaseDatabaseWrapper(DatabaseDependencies dependencies) : base(dependencies)
    {
    }

    public override int SaveChanges(IList<IUpdateEntry> entries)
    {
        throw new NotImplementedException();
    }

    public override Task<int> SaveChangesAsync(IList<IUpdateEntry> entries, CancellationToken cancellationToken = new CancellationToken())
    {
        throw new NotImplementedException();
    }
    
}