using Microsoft.EntityFrameworkCore.Query;

namespace Couchbase.EntityFrameworkCore.Query.Internal.Translators;

public class CouchbaseMethodCallTranslatorProvider : RelationalMethodCallTranslatorProvider
{
    /*From https://github.com/dotnet/efcore/blob/73db4f0f5b5e599435a7e8cfda4a4066b8ada420/src/EFCore.Sqlite.Core/Query/Internal/SqliteMethodCallTranslatorProvider.cs*/
    
    public CouchbaseMethodCallTranslatorProvider(RelationalMethodCallTranslatorProviderDependencies dependencies) : base(dependencies)
    {
        var sqlExpressionFactory =/* (SqliteSqlExpressionFactory)*/dependencies.SqlExpressionFactory;

        AddTranslators(
            new IMethodCallTranslator[]
            {
                /*new SqliteByteArrayMethodTranslator(sqlExpressionFactory),
                new SqliteCharMethodTranslator(sqlExpressionFactory),
                new SqliteDateOnlyMethodTranslator(sqlExpressionFactory),
                new SqliteDateTimeMethodTranslator(sqlExpressionFactory),
                new SqliteGlobMethodTranslator(sqlExpressionFactory),
                new SqliteHexMethodTranslator(sqlExpressionFactory),
                new SqliteMathTranslator(sqlExpressionFactory),
                new SqliteObjectToStringTranslator(sqlExpressionFactory),
                new SqliteRandomTranslator(sqlExpressionFactory),
                new SqliteRegexMethodTranslator(sqlExpressionFactory),*/
                new CouchbaseStringMethodTranslator(sqlExpressionFactory),
               // new SqliteSubstrMethodTranslator(sqlExpressionFactory)
            });
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
