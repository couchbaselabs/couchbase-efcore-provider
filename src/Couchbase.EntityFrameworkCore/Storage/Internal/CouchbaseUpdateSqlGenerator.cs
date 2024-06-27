using System.Text;
using Microsoft.EntityFrameworkCore.Update;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseUpdateSqlGenerator : UpdateSqlGenerator
{
    public CouchbaseUpdateSqlGenerator(UpdateSqlGeneratorDependencies dependencies) : base(dependencies)
    {
    }
}