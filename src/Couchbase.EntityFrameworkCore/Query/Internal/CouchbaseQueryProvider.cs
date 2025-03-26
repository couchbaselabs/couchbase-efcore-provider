using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseQueryProvider : IAsyncQueryProvider, IQueryProvider
{
    private readonly QuerySqlGenerator _querySqlGenerator;
    private readonly IQueryCompiler _queryCompiler;
    private static MethodInfo? _genericCreateQueryMethod;
    private MethodInfo? _genericExecuteMethod;
    private IDatabase _database;
    private IQueryContextFactory _queryContextFactory;
    private IEvaluatableExpressionFilter _evaluatableExpressionFilter;
    private IDiagnosticsLogger<Microsoft.EntityFrameworkCore.DbLoggerCategory.Query> _logger;

    public CouchbaseQueryProvider(QuerySqlGenerator querySqlGenerator, IQueryCompiler queryCompiler, IDatabase _database, IQueryContextFactory queryContextFactory, IEvaluatableExpressionFilter evaluatableExpressionFilter, IDiagnosticsLogger<Microsoft.EntityFrameworkCore.DbLoggerCategory.Query> logger)
    {
        _querySqlGenerator = querySqlGenerator;
        _queryCompiler = queryCompiler;
        this._database = _database;
        _queryContextFactory = queryContextFactory;
        _evaluatableExpressionFilter = evaluatableExpressionFilter;
        _logger = logger;
    }
    
    private static MethodInfo GenericCreateQueryMethod
        => _genericCreateQueryMethod ??= typeof(EntityQueryProvider)
            .GetMethod("CreateQuery", 1, BindingFlags.Instance | BindingFlags.Public, null, [typeof(Expression)], null)!;

    private MethodInfo GenericExecuteMethod
        => _genericExecuteMethod ??= _queryCompiler.GetType()
            .GetMethod("Execute", 1, BindingFlags.Instance | BindingFlags.Public, null, [typeof(Expression)], null)!;

 public virtual IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        => new EntityQueryable<TElement>(this, expression);
 
    public virtual IQueryable CreateQuery(Expression expression)
        => (IQueryable)GenericCreateQueryMethod
            .MakeGenericMethod(expression.Type.GetSequenceType())
            .Invoke(this, [expression])!;

    public virtual TResult Execute<TResult>(Expression expression)
    {
        var result = _queryCompiler.Execute<TResult>(expression);
        return _queryCompiler.Execute<TResult>(expression);
    }


    public virtual object Execute(Expression expression)
        => GenericExecuteMethod.MakeGenericMethod(expression.Type)
            .Invoke(_queryCompiler, [expression])!;

   
    public virtual TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
        => _queryCompiler.ExecuteAsync<TResult>(expression, cancellationToken);
    
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
