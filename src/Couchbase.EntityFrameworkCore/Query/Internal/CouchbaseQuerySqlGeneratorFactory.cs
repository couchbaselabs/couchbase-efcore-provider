using System.Text.Json;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseQuerySqlGeneratorFactory : IQuerySqlGeneratorFactory
{
    private readonly QuerySqlGeneratorDependencies _dependencies;
    private readonly JsonNamingPolicy? _fieldNamingPolicy;

    // JsonNamingPolicy? is captured by value at DI-registration time (see
    // CouchbaseServiceCollectionExtensions.AddEntityFrameworkCouchbase) rather than resolving
    // ICouchbaseDbContextOptionsBuilder here directly -- IQuerySqlGeneratorFactory is
    // Singleton-lifetime (EF Core's own registration), while ICouchbaseDbContextOptionsBuilder is
    // Scoped, so constructor-injecting the latter would be a captive-dependency bug.
    public CouchbaseQuerySqlGeneratorFactory(QuerySqlGeneratorDependencies dependencies, JsonNamingPolicy? fieldNamingPolicy)
    {
        _dependencies = dependencies;
        _fieldNamingPolicy = fieldNamingPolicy;
    }

    public QuerySqlGenerator Create()
    {
        return new CouchbaseQuerySqlGenerator(_dependencies, _fieldNamingPolicy);
    }
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
