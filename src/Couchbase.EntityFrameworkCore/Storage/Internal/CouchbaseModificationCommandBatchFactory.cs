using Microsoft.EntityFrameworkCore.Update;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseModificationCommandBatchFactory : IModificationCommandBatchFactory
{
    private ModificationCommandBatchFactoryDependencies _dependencies;
    public CouchbaseModificationCommandBatchFactory(ModificationCommandBatchFactoryDependencies
        dependencies)
    {
        _dependencies = dependencies;
    }
    public ModificationCommandBatch Create()
    {
        //SingleModificationCommandBatch
        return new CouchbaseModificationCommandBatch(_dependencies);
    }
}