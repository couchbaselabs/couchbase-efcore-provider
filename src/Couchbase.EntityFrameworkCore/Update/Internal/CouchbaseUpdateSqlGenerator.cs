using System.Text;
using Microsoft.EntityFrameworkCore.Update;

namespace Couchbase.EntityFrameworkCore.Update.Internal;

public class CouchbaseUpdateSqlGenerator : IUpdateSqlGenerator
{
    public string GenerateNextSequenceValueOperation(string name, string? schema)
    {
        throw new NotImplementedException();
    }

    public void AppendNextSequenceValueOperation(StringBuilder commandStringBuilder, string name, string? schema)
    {
        throw new NotImplementedException();
    }

    public string GenerateObtainNextSequenceValueOperation(string name, string? schema)
    {
        throw new NotImplementedException();
    }

    public void AppendObtainNextSequenceValueOperation(StringBuilder commandStringBuilder, string name, string? schema)
    {
        throw new NotImplementedException();
    }

    public void AppendBatchHeader(StringBuilder commandStringBuilder)
    {
        throw new NotImplementedException();
    }

    public void PrependEnsureAutocommit(StringBuilder commandStringBuilder)
    {
        throw new NotImplementedException();
    }

    public ResultSetMapping AppendDeleteOperation(StringBuilder commandStringBuilder, IReadOnlyModificationCommand command,
        int commandPosition, out bool requiresTransaction)
    {
        throw new NotImplementedException();
    }

    public ResultSetMapping AppendInsertOperation(StringBuilder commandStringBuilder, IReadOnlyModificationCommand command,
        int commandPosition, out bool requiresTransaction)
    {
        throw new NotImplementedException();
    }

    public ResultSetMapping AppendUpdateOperation(StringBuilder commandStringBuilder, IReadOnlyModificationCommand command,
        int commandPosition, out bool requiresTransaction)
    {
        throw new NotImplementedException();
    }

    public ResultSetMapping AppendStoredProcedureCall(StringBuilder commandStringBuilder, IReadOnlyModificationCommand command,
        int commandPosition, out bool requiresTransaction)
    {
        throw new NotImplementedException();
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
