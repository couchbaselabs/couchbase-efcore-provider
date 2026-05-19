using System.Runtime.CompilerServices;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

// Stores the original collection-object reference for each OwnsMany navigation on a just-
// materialised entity. The database wrapper's save path compares the current reference to the
// stored one: if they differ the owner document must be rewritten even when EF Core's own
// change tracker produced no owned-item entries (e.g. customer.ContactMethods = []).
//
// ConditionalWeakTable uses weak keys so entities that fall out of scope are GC'd without any
// manual cleanup.
internal static class OwnedCollectionSnapshot
{
    internal static readonly ConditionalWeakTable<object, Dictionary<string, object?>> OriginalRefs = new();
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
