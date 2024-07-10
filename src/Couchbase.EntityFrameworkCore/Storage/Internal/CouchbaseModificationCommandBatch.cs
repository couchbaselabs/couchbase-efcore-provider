using Couchbase.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseModificationCommandBatch : ModificationCommandBatch
{
    ModificationCommandBatchFactoryDependencies _dependencies;
    
    public CouchbaseModificationCommandBatch(ModificationCommandBatchFactoryDependencies dependencies)
    {
        _dependencies = dependencies;
    }
    public override IReadOnlyList<IReadOnlyModificationCommand> ModificationCommands { get; }
    public override bool TryAddCommand(IReadOnlyModificationCommand modificationCommand)
    {
        throw new NotImplementedException();
    }

    public override void Complete(bool moreBatchesExpected)
    {
        throw new NotImplementedException();
    }

    public override bool RequiresTransaction { get; }
    public override bool AreMoreBatchesExpected { get; }
    public override void Execute(IRelationalConnection connection)
    {
        throw new NotImplementedException();
    }

    public override Task ExecuteAsync(IRelationalConnection connection, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}