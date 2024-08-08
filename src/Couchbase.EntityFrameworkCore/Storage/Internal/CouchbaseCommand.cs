using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseCommand : RelationalCommand
{
  public CouchbaseCommand(RelationalCommandBuilderDependencies dependencies, string commandText, IReadOnlyList<IRelationalParameter> parameters) 
    : base(dependencies, commandText, parameters)
  {
  }

  public override DbCommand CreateDbCommand(RelationalCommandParameterObject parameterObject, Guid commandId,
    DbCommandMethod commandMethod)
  {
    return base.CreateDbCommand(parameterObject, commandId, commandMethod);
  }
}