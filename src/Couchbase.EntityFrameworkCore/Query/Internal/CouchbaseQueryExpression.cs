using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseQueryExpression : Expression
{
    private readonly IEntityType _entityType;

    public CouchbaseQueryExpression(IEntityType entityType)
    {
        _entityType = entityType;
    }
}