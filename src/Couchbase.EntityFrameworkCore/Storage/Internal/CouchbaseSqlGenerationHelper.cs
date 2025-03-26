using System.Text;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseSqlGenerationHelper : RelationalSqlGenerationHelper
{
    public CouchbaseSqlGenerationHelper(RelationalSqlGenerationHelperDependencies dependencies) : base(dependencies)
    {
    }

    public override void DelimitIdentifier(StringBuilder builder, string identifier)
    {
        builder.Append('`');
        EscapeIdentifier(builder, identifier);
        builder.Append('`');
    }
    
   public override string DelimitIdentifier(string identifier)
        => $"`{EscapeIdentifier(identifier)}`";

   public override string GenerateParameterName(string name) =>
       name.StartsWith("$", StringComparison.Ordinal)
       ? name : "$" + name;

   public override void GenerateParameterName(StringBuilder builder, string name)
       => builder.Append('$').Append(name);
}

/* ************************************************************
 *
 *    @author Couchbase <info@couchbase.com>
 *    @copyright 2025 Couchbase, Inc.
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
