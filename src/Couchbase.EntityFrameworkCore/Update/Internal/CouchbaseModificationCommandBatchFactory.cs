using Microsoft.EntityFrameworkCore.Update;

namespace Couchbase.EntityFrameworkCore.Update.Internal;

public class CouchbaseModificationCommandBatchFactory : IModificationCommandBatchFactory
{
    public ModificationCommandBatch Create()
    {
        return new CouchbaseModificationCommandBatch();
    }
}