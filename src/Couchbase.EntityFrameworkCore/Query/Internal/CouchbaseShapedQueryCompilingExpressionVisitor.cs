using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Storage.Json;
using static System.Linq.Expressions.Expression;


namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseShapedQueryCompilingExpressionVisitor : RelationalShapedQueryCompilingExpressionVisitor
{
    private readonly QuerySqlGenerator _querySqlGenerator;
    private readonly IClusterProvider _clusterProvider;
    private readonly RelationalCommandCache _relationalCommandCache;
    private readonly Type _contextType;
    private readonly ISet<string> _tags;
    private readonly bool _threadSafetyChecksEnabled;
    private bool _detailedErrorsEnabled;
    private readonly bool _useRelationalNulls;
    
    public CouchbaseShapedQueryCompilingExpressionVisitor(ShapedQueryCompilingExpressionVisitorDependencies dependencies,
        RelationalShapedQueryCompilingExpressionVisitorDependencies relationalDependencies,
        QueryCompilationContext queryCompilationContext, 
        QuerySqlGenerator querySqlGenerator, 
        IClusterProvider clusterProvider)
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
        var selectExpression = (SelectExpression)shapedQueryExpression.QueryExpression;
        var nonComposedFromSql = selectExpression.IsNonComposedFromSql();
        var querySplittingBehavior = ((RelationalQueryCompilationContext)QueryCompilationContext).QuerySplittingBehavior;
        var splitQuery = querySplittingBehavior == QuerySplittingBehavior.SplitQuery;
        var collectionCount = 0;
        
        var shaper = new ShaperProcessingExpressionVisitor(this, selectExpression, _tags, splitQuery, nonComposedFromSql).ProcessShaper(
            shapedQueryExpression.ShaperExpression,
            out var relationalCommandCache, out var readerColumns, out var relatedDataLoaders, ref collectionCount);
     
       return New(typeof(CouchbaseQueryEnumerable<>).MakeGenericType(shaper.ReturnType).GetConstructors()[0],
           Convert(QueryCompilationContext.QueryContextParameter, typeof(RelationalQueryContext)),
           Constant(relationalCommandCache),
           Constant(_clusterProvider));
    }

    private sealed class ShaperProcessingExpressionVisitor : ExpressionVisitor
    {
        private int _collectionId;
        private ParameterExpression _indexMapParameter;
        private readonly List<Expression> _expressions = new();
        private List<ParameterExpression>? _variables = new();
        private readonly CouchbaseShapedQueryCompilingExpressionVisitor _parentVisitor;
        private SelectExpression _selectExpression;
        private IReadOnlyList<ReaderColumn?> _readerColumns;
        private ParameterExpression _dataReaderParameter;
        private bool _containsCollectionMaterialization;
        private bool _generateCommandCache;
        private List<Expression> _includeExpressions = new();
        private List<Expression> _jsonEntityExpressions = new();
        private ParameterExpression _resultContextParameter;
        private ParameterExpression _resultCoordinatorParameter;
        private MemberExpression _valuesArrayExpression;
        private bool _splitQuery;
        private List<Expression> _collectionPopulatingExpressions;
        private List<Expression> _valuesArrayInitializers;
        private bool _isAsync;
        private readonly ParameterExpression? _executionStrategyParameter;
        private readonly IDiagnosticsLogger<DbLoggerCategory.Query> _queryLogger;
        private readonly bool _isTracking;
        private readonly ISet<string> _tags;
        private readonly bool _detailedErrorsEnabled;
        
        public ShaperProcessingExpressionVisitor(
            CouchbaseShapedQueryCompilingExpressionVisitor parentVisitor,
            SelectExpression selectExpression,
            ISet<string> tags,
            bool splitQuery,
            bool indexMap)
        {
            _parentVisitor = parentVisitor;
            _queryLogger = parentVisitor.QueryCompilationContext.Logger;
            _resultCoordinatorParameter = Parameter(
                splitQuery ? typeof(SplitQueryResultCoordinator) : typeof(SingleQueryResultCoordinator), "resultCoordinator");
            _executionStrategyParameter = splitQuery ? Parameter(typeof(IExecutionStrategy), "executionStrategy") : null;

            _selectExpression = selectExpression;
            _tags = tags;
            _dataReaderParameter = Parameter(typeof(DbDataReader), "dataReader");
            _resultContextParameter = Parameter(typeof(ResultContext), "resultContext");
            _indexMapParameter = indexMap ? Parameter(typeof(int[]), "indexMap") : null;
            if (parentVisitor.QueryCompilationContext.IsBuffering)
            {
                _readerColumns = new ReaderColumn?[_selectExpression.Projection.Count];
            }

            _generateCommandCache = true;
            _detailedErrorsEnabled = parentVisitor._detailedErrorsEnabled;
            _isTracking = parentVisitor.QueryCompilationContext.QueryTrackingBehavior == QueryTrackingBehavior.TrackAll;
            _isAsync = parentVisitor.QueryCompilationContext.IsAsync;
            _splitQuery = splitQuery;

            _selectExpression.ApplyTags(_tags);
        }

        public LambdaExpression ProcessShaper(
            Expression shaperExpression,
            out RelationalCommandCache? relationalCommandCache,
            out IReadOnlyList<ReaderColumn?>? readerColumns,
            out LambdaExpression? relatedDataLoaders,
            ref int collectionId)
        {
            relatedDataLoaders = null;
            _collectionId = collectionId;

            if (_indexMapParameter != null)
            {
                var result = Visit(shaperExpression);
                _expressions.Add(result);
                result = Block(_variables, _expressions);

                relationalCommandCache = new RelationalCommandCache(
                    _parentVisitor.Dependencies.MemoryCache,
                    _parentVisitor.RelationalDependencies.QuerySqlGeneratorFactory,
                    _parentVisitor.RelationalDependencies.RelationalParameterBasedSqlProcessorFactory,
                    _selectExpression,
                    _parentVisitor._useRelationalNulls);
                readerColumns = _readerColumns;

                return Lambda(
                    result,
                    QueryCompilationContext.QueryContextParameter,
                    _dataReaderParameter,
                    _indexMapParameter);
            }

            _containsCollectionMaterialization = new CollectionShaperFindingExpressionVisitor()
                .ContainsCollectionMaterialization(shaperExpression);

            if (!_containsCollectionMaterialization)
            {
                var result = Visit(shaperExpression);
                _expressions.AddRange(_includeExpressions);
                _expressions.AddRange(_jsonEntityExpressions);
                _expressions.Add(result);
                result = Block(_variables, _expressions);

                relationalCommandCache = _generateCommandCache
                    ? new RelationalCommandCache(
                        _parentVisitor.Dependencies.MemoryCache,
                        _parentVisitor.RelationalDependencies.QuerySqlGeneratorFactory,
                        _parentVisitor.RelationalDependencies.RelationalParameterBasedSqlProcessorFactory,
                        _selectExpression,
                        _parentVisitor._useRelationalNulls)
                    : null;
                readerColumns = _readerColumns;

                return Lambda(
                    result,
                    QueryCompilationContext.QueryContextParameter,
                    _dataReaderParameter,
                    _resultContextParameter,
                    _resultCoordinatorParameter);
            }
            else
            {
                _valuesArrayExpression = MakeMemberAccess(_resultContextParameter, ResultContextValuesMemberInfo);
                _collectionPopulatingExpressions = new List<Expression>();
                _valuesArrayInitializers = new List<Expression>();

                var result = Visit(shaperExpression);

                var valueArrayInitializationExpression = Assign(
                    _valuesArrayExpression, NewArrayInit(typeof(object), _valuesArrayInitializers));

                _expressions.AddRange(_jsonEntityExpressions);
                _expressions.Add(valueArrayInitializationExpression);
                _expressions.AddRange(_includeExpressions);

                if (_splitQuery)
                {
                    _expressions.Add(Default(result.Type));

                    var initializationBlock = Block(_variables, _expressions);
                    result = Condition(
                        Equal(_valuesArrayExpression, Constant(null, typeof(object[]))),
                        initializationBlock,
                        result);

                    if (_isAsync)
                    {
                        var tasks = NewArrayInit(
                            typeof(Func<Task>), _collectionPopulatingExpressions.Select(
                                e => Lambda<Func<Task>>(e)));
                        relatedDataLoaders =
                            Lambda<Func<QueryContext, IExecutionStrategy, SplitQueryResultCoordinator, Task>>(
                                Call(TaskAwaiterMethodInfo, tasks),
                                QueryCompilationContext.QueryContextParameter,
                                _executionStrategyParameter!,
                                _resultCoordinatorParameter);
                    }
                    else
                    {
                        relatedDataLoaders =
                            Lambda<Action<QueryContext, IExecutionStrategy, SplitQueryResultCoordinator>>(
                                Block(_collectionPopulatingExpressions),
                                QueryCompilationContext.QueryContextParameter,
                                _executionStrategyParameter!,
                                _resultCoordinatorParameter);
                    }
                }
                else
                {
                    var initializationBlock = Block(_variables, _expressions);

                    var conditionalMaterializationExpressions = new List<Expression>
                    {
                        IfThen(
                            Equal(_valuesArrayExpression, Constant(null, typeof(object[]))),
                            initializationBlock)
                    };

                    conditionalMaterializationExpressions.AddRange(_collectionPopulatingExpressions);

                    conditionalMaterializationExpressions.Add(
                        Condition(
                            IsTrue(
                                MakeMemberAccess(
                                    _resultCoordinatorParameter, SingleQueryResultCoordinatorResultReadyMemberInfo)),
                            result,
                            Default(result.Type)));

                    result = Block(conditionalMaterializationExpressions);
                }

                relationalCommandCache = _generateCommandCache
                    ? new RelationalCommandCache(
                        _parentVisitor.Dependencies.MemoryCache,
                        _parentVisitor.RelationalDependencies.QuerySqlGeneratorFactory,
                        _parentVisitor.RelationalDependencies.RelationalParameterBasedSqlProcessorFactory,
                        _selectExpression,
                        _parentVisitor._useRelationalNulls)
                    : null;
                readerColumns = _readerColumns;

                collectionId = _collectionId;

                return Lambda(
                    result,
                    QueryCompilationContext.QueryContextParameter,
                    _dataReaderParameter,
                    _resultContextParameter,
                    _resultCoordinatorParameter);
            }
        }

        private static readonly MemberInfo ResultContextValuesMemberInfo
            = typeof(ResultContext).GetMember(nameof(ResultContext.Values))[0];
        
        private static readonly MemberInfo SingleQueryResultCoordinatorResultReadyMemberInfo
            = typeof(SingleQueryResultCoordinator).GetMember(nameof(SingleQueryResultCoordinator.ResultReady))[0];
        
        private static readonly MethodInfo TaskAwaiterMethodInfo
            = typeof(ShaperProcessingExpressionVisitor).GetTypeInfo().GetDeclaredMethod(nameof(TaskAwaiter))!;
    }
    
    private sealed class CollectionShaperFindingExpressionVisitor : ExpressionVisitor
    {
        private bool _containsCollection;

        public bool ContainsCollectionMaterialization(Expression expression)
        {
            _containsCollection = false;

            Visit(expression);

            return _containsCollection;
        }

        [return: NotNullIfNotNull("expression")]
        public override Expression? Visit(Expression? expression)
        {
            if (_containsCollection)
            {
                return expression;
            }

            if (expression is RelationalCollectionShaperExpression or RelationalSplitCollectionShaperExpression)
            {
                _containsCollection = true;

                return expression;
            }

            return base.Visit(expression);
        }
    }
}