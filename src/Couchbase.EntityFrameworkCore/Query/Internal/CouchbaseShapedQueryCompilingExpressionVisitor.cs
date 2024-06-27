using System.Reflection.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using System.Linq.Expressions;
using Couchbase.Extensions.DependencyInjection;
using static System.Linq.Expressions.Expression;


namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseShapedQueryCompilingExpressionVisitor : RelationalShapedQueryCompilingExpressionVisitor
{
    private readonly QuerySqlGenerator _querySqlGenerator;
    private readonly IClusterProvider _clusterProvider;
    private readonly Type _contextType;
    private readonly ISet<string> _tags;
    private readonly bool _threadSafetyChecksEnabled;
    private readonly bool _detailedErrorsEnabled;
    private readonly bool _useRelationalNulls;
    
    public CouchbaseShapedQueryCompilingExpressionVisitor(ShapedQueryCompilingExpressionVisitorDependencies dependencies,
        RelationalShapedQueryCompilingExpressionVisitorDependencies relationalDependencies,
    QueryCompilationContext queryCompilationContext, QuerySqlGenerator querySqlGenerator, IClusterProvider clusterProvider)
    : base(dependencies, relationalDependencies, queryCompilationContext)
    {
        _querySqlGenerator = querySqlGenerator;
        _clusterProvider = clusterProvider;
        RelationalDependencies = relationalDependencies;

        _contextType = queryCompilationContext.ContextType;
        _tags = queryCompilationContext.Tags;
        _threadSafetyChecksEnabled = dependencies.CoreSingletonOptions.AreThreadSafetyChecksEnabled;
        _detailedErrorsEnabled = dependencies.CoreSingletonOptions.AreDetailedErrorsEnabled;
        _useRelationalNulls = RelationalOptionsExtension.Extract(queryCompilationContext.ContextOptions).UseRelationalNulls;
    }
    
    protected override RelationalShapedQueryCompilingExpressionVisitorDependencies RelationalDependencies { get; }

    protected override Expression VisitShapedQuery(ShapedQueryExpression shapedQueryExpression)
    {
       var command = _querySqlGenerator.GetCommand(shapedQueryExpression);
     
       return New(typeof(CouchbaseQueryEnumerable<>).MakeGenericType(shapedQueryExpression.Type).GetConstructors()[0],
           Constant(command),
           Constant(_clusterProvider));
    }
}