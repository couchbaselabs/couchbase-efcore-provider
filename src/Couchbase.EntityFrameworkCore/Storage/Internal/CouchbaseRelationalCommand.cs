using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseRelationalCommand : RelationalCommand
{
  public CouchbaseRelationalCommand(RelationalCommandBuilderDependencies dependencies, string commandText, IReadOnlyList<IRelationalParameter> parameters) 
    : base(dependencies, commandText, parameters)
  {
  }

  public override DbCommand CreateDbCommand(RelationalCommandParameterObject parameterObject, Guid commandId,
    DbCommandMethod commandMethod)
  {
    return base.CreateDbCommand(parameterObject, commandId, commandMethod);
  }
}

/* ************************************************************
 *
 *    @author Couchbase <info@couchbase.com>
 *    @copyright 2021 Couchbase, Inc.
 *
 *    Licensed under the Apache License, Version 2.0 (the "License");
 *    you may not use this file except in compliance with the License.
 *    You may obtain a copy of the License at
 *
 *        http://www.apache.org/licenses/LICENSE-2.0
 *
 *    Unless required by applicable law or agreed to in writing, software
 *    distributed under the License is distributed on an "AS IS" BASIS,
 *    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *    See the License for the specific language governing permissions and
 *    limitations under the License.
 *
 * ************************************************************/
