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
    
    /*public virtual Expression ExtractParameters(
        Expression query,
        IParameterValues parameterValues,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger,
        bool parameterize = true,
        bool generateContextAccessors = false)
    {
        
        return new ParameterExtractingExpressionVisitor(this._evaluatableExpressionFilter, parameterValues, this._contextType, this._model, logger, parameterize, generateContextAccessors).ExtractParameters(query);
    }
    
    public virtual Func<QueryContext, TResult> CreateCompiledAsyncQuery<TResult>(Expression query)
    {
        query = this.ExtractParameters(query, (IParameterValues) this._queryContextFactory.Create(), this._logger, false);
        return  _database.CompileQuery<TResult>(query, false);
    }*/
}