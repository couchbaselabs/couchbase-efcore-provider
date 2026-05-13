using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Query;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

public class CouchbaseQueryCompilationContext : RelationalQueryCompilationContext
{
    public CouchbaseQueryCompilationContext(
        QueryCompilationContextDependencies dependencies,
        RelationalQueryCompilationContextDependencies relationalDependencies,
        bool async)
        : base(dependencies, relationalDependencies, async)
    {
    }

    [Experimental("EF9100")]
    public CouchbaseQueryCompilationContext(
        QueryCompilationContextDependencies dependencies,
        RelationalQueryCompilationContextDependencies relationalDependencies,
        bool async,
        bool precompiling)
        : base(dependencies, relationalDependencies, async, precompiling)
    {
    }

    /// <summary>
    /// Navigation includes recorded during shaper compilation (Phase 2).
    /// Populated by <see cref="CouchbaseShapedQueryCompilingExpressionVisitor"/> and
    /// consumed by Phase 4 result-shaping logic.
    /// </summary>
    public List<NavigationInclude> NavigationIncludes { get; } = [];
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
