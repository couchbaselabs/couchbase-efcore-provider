using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseShapedQueryCompilingExpressionVisitorFactory : IShapedQueryCompilingExpressionVisitorFactory
{
    private readonly ShapedQueryCompilingExpressionVisitorDependencies _dependencies;
    private readonly RelationalShapedQueryCompilingExpressionVisitorDependencies _relationalDependencies;
    private readonly IQuerySqlGeneratorFactory _querySqlGeneratorFactory;
    private readonly ICouchbaseDbContextOptionsBuilder _couchbaseDbContextOptionsBuilder;
    private readonly IBucketProvider _bucketProvider;

    public CouchbaseShapedQueryCompilingExpressionVisitorFactory(
        ShapedQueryCompilingExpressionVisitorDependencies dependencies, 
        RelationalShapedQueryCompilingExpressionVisitorDependencies relationalDependencies,
        IQuerySqlGeneratorFactory querySqlGeneratorFactory,
        IBucketProvider bucketProvider,
        ICouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder)
    {
        _dependencies = dependencies;
        _relationalDependencies = relationalDependencies;
        _querySqlGeneratorFactory = querySqlGeneratorFactory;
        _couchbaseDbContextOptionsBuilder = couchbaseDbContextOptionsBuilder;
        _bucketProvider = bucketProvider;
    }

    public ShapedQueryCompilingExpressionVisitor Create(QueryCompilationContext queryCompilationContext)
    {
        //the QuerySqlGenerator may be cacheable - if so make a field and call create in ctor above
        return new CouchbaseShapedQueryCompilingExpressionVisitor(
            _dependencies,
            _relationalDependencies,
            queryCompilationContext,
            _querySqlGeneratorFactory.Create(),
            _bucketProvider,
            _couchbaseDbContextOptionsBuilder);
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
