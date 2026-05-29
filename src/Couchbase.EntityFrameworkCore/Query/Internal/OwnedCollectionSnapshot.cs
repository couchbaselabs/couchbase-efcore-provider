using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

// Stores the original collection-object reference and per-item property value snapshots for
// every OwnsMany navigation on a just-materialised entity.
//
// OriginalRefs — detects reference replacement (customer.ContactMethods = []).
// OriginalItems — detects in-place content changes: Add, Remove, or scalar mutation.
//
// ConditionalWeakTable uses weak keys so entities that fall out of scope are GC'd without any
// manual cleanup.
internal static class OwnedCollectionSnapshot
{
    // nav.Name → original collection object reference
    internal static readonly ConditionalWeakTable<object, Dictionary<string, object?>> OriginalRefs = new();

    // nav.Name → ordered list of per-item property value dictionaries (prop.Name → value)
    internal static readonly ConditionalWeakTable<object, Dictionary<string, IReadOnlyList<Dictionary<string, object?>>>> OriginalItems = new();

    // EF Core model metadata is immutable after OnModelCreating completes. Cache the
    // non-shadow property list per navigation so it is computed at most once per navigation
    // for the process lifetime, rather than on every materialisation and every SaveChanges.
    private static readonly ConcurrentDictionary<INavigation, IReadOnlyList<IProperty>> _trackedPropsCache = new();

    internal static IReadOnlyList<IProperty> GetTrackedProperties(INavigation nav)
        => _trackedPropsCache.GetOrAdd(nav, static n =>
            n.TargetEntityType.GetProperties()
                .Where(p => !p.IsShadowProperty())
                .ToArray());
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
