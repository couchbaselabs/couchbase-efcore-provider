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