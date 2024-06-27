using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;

namespace Couchbase.EntityFrameworkCore.Update.Internal;

public class CouchbaseModificationCommandBatch: ModificationCommandBatch 
{
    public override bool TryAddCommand(IReadOnlyModificationCommand modificationCommand)
    {
        throw new NotImplementedException();
    }

    public override void Complete(bool moreBatchesExpected)
    {
        throw new NotImplementedException();
    }

    public override void Execute(IRelationalConnection connection)
    {
        throw new NotImplementedException();
    }

    public override Task ExecuteAsync(IRelationalConnection connection, CancellationToken cancellationToken = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public override IReadOnlyList<IReadOnlyModificationCommand> ModificationCommands { get; }
    public override bool RequiresTransaction { get; }
    public override bool AreMoreBatchesExpected { get; }
}