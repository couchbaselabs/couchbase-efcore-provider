using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseDataReader : RelationalDataReader
{
    public override Task<bool> ReadAsync(CancellationToken cancellationToken = new CancellationToken())
    {
        return base.ReadAsync(cancellationToken);
    }
}