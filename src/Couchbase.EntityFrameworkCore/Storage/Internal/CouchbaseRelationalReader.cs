using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseRelationalReader : RelationalDataReader
{
    public override void Initialize(IRelationalConnection relationalConnection, DbCommand command, DbDataReader reader, Guid commandId,
        IRelationalCommandDiagnosticsLogger? logger)
    {
        base.Initialize(relationalConnection, command, reader, commandId, logger);
    }
}