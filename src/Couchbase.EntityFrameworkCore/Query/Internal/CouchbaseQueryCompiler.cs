using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseQueryCompiler : IQueryCompiler
{
    public TResult Execute<TResult>(Expression query)
    {
        throw new NotImplementedException();
    }

    public TResult ExecuteAsync<TResult>(Expression query, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Func<QueryContext, TResult> CreateCompiledQuery<TResult>(Expression query)
    {
        throw new NotImplementedException();
    }

    public Func<QueryContext, TResult> CreateCompiledAsyncQuery<TResult>(Expression query)
    {
        throw new NotImplementedException();
    }
}