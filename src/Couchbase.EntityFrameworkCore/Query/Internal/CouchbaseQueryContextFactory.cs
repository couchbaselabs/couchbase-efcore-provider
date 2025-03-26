using Couchbase.EntityFrameworkCore.Storage.Internal;
using Microsoft.EntityFrameworkCore.Query;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseQueryContextFactory : IQueryContextFactory
{
    private readonly ICouchbaseClientWrapper _couchbaseClient;

    public CouchbaseQueryContextFactory(QueryContextDependencies contextDependencies,
        ICouchbaseClientWrapper couchbaseClient)
    {
        _couchbaseClient = couchbaseClient;
        Dependencies = contextDependencies;
    }
    
    protected virtual QueryContextDependencies Dependencies { get; }
    public QueryContext Create() => new CouchbaseQueryContext(Dependencies, _couchbaseClient);
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
