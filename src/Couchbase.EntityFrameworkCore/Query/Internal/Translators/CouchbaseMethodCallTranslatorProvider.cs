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