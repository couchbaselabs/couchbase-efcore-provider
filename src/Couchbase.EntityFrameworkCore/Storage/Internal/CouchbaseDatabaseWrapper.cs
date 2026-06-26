using System.Collections;
using System.Text.Json;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Internal;
using Couchbase.EntityFrameworkCore.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Database = Microsoft.EntityFrameworkCore.Storage.Database;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseDatabaseWrapper : Database
{
    private readonly ICouchbaseClientWrapper _couchbaseClient;
    private readonly IRelationalConnection _relationalConnection;
    private readonly JsonNamingPolicy? _fieldNamingPolicy;

    public CouchbaseDatabaseWrapper(
        DatabaseDependencies dependencies,
        ICouchbaseClientWrapper couchbaseClient,
        IRelationalConnection relationalConnection,
        ICouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder)
        : base(dependencies)
    {
        _couchbaseClient = couchbaseClient ?? throw new ArgumentNullException(nameof(couchbaseClient));
        _relationalConnection = relationalConnection ?? throw new ArgumentNullException(nameof(relationalConnection));
        _fieldNamingPolicy = couchbaseDbContextOptionsBuilder.FieldNamingPolicy;
    }

    public override int SaveChanges(IList<IUpdateEntry> entries)
    {
#if DEBUG
        return AsyncHelper.RunSync(
            static state => state.self.SaveChangesAsync(state.entries),
            (self: this, entries));
#else
        throw ExceptionHelper.SyncroIONotSupportedException();
#endif
    }

    public override async Task<int> SaveChangesAsync(IList<IUpdateEntry> entries, CancellationToken cancellationToken = new())
    {
        var transaction = GetCurrentTransaction();

        // Track root-entity keys written in the main loop so the second pass can skip
        // owners that were already written (e.g. user manually set entry.State = Modified).
        var writtenRoots = new HashSet<string>();
        // Owned entries whose owner must be written in the second pass.
        var deferredOwnedEntries = new List<IUpdateEntry>();
        // Writes are collected first (building the document is CPU-only) and dispatched together at
        // the end, so the non-transactional path can run independent writes concurrently instead of
        // one blocking round-trip per changed entity.
        var pendingWrites = new List<PendingWrite>();

        foreach (var updateEntry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entityEntry = updateEntry.ToEntityEntry();
            var entity = entityEntry.Entity;
            var entityType = updateEntry.EntityType;

            if (entityType.IsOwned())
            {
                // Collect owned entries with actual changes; their owners will be written
                // in the second pass below if not already written by the main loop.
                if (updateEntry.EntityState is not (EntityState.Unchanged or EntityState.Detached))
                    deferredOwnedEntries.Add(updateEntry);
                continue;
            }

            // Shared entity types (e.g. hidden join table from HasMany().WithMany()) have
            // all PK columns as shadow properties — use the IUpdateEntry overload so the
            // values are read from the entry rather than the CLR Dictionary<string,object>.
            var primaryKey = entityType.HasSharedClrType
                ? entityType.GetPrimaryKey(updateEntry)
                : entityType.GetPrimaryKey(entity);
            var keyspace = entityType.GetCollectionName()
                ?? throw new InvalidOperationException(
                    $"Entity type '{entityType.ClrType.Name}' has no mapped table name. " +
                    "Ensure the entity is mapped to a Couchbase collection via ToCouchbaseCollection().");

            switch (updateEntry.EntityState)
            {
                case EntityState.Detached:
                case EntityState.Unchanged:
                    // Not written — intentionally NOT added to writtenRoots so the deferred-owned
                    // pass can still write the owner if an owned item triggered a change.
                    break;

                case EntityState.Deleted:
                    // Add to writtenRoots so the deferred pass doesn't upsert a just-deleted doc.
                    writtenRoots.Add($"{entityType.ClrType.Name}:{primaryKey}");
                    pendingWrites.Add(new PendingWrite(CouchbaseWriteKind.Delete, primaryKey, keyspace, null));
                    break;

                case EntityState.Modified:
                    writtenRoots.Add($"{entityType.ClrType.Name}:{primaryKey}");
                    pendingWrites.Add(new PendingWrite(CouchbaseWriteKind.Upsert, primaryKey, keyspace,
                        HydrateObjectFromEntity(updateEntry, _fieldNamingPolicy)));
                    break;

                case EntityState.Added:
                    writtenRoots.Add($"{entityType.ClrType.Name}:{primaryKey}");
                    pendingWrites.Add(new PendingWrite(CouchbaseWriteKind.Insert, primaryKey, keyspace,
                        HydrateObjectFromEntity(updateEntry, _fieldNamingPolicy)));
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        // Second pass: for each owned entry with a state change, write the owner document
        // if it was not already written in the main loop above. This removes the need for
        // callers to manually set ctx.Entry(owner).State = EntityState.Modified after
        // mutating owned navigations (OwnsOne property changes, OwnsMany collection mutation).
        foreach (var ownedEntry in deferredOwnedEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Walk the ownership chain up to the root non-owned entity.
            // Deeply nested owned types (e.g. ContactTag → ContactMethod → Customer) require
            // multiple hops; stop at the first principal whose entity type is not owned —
            // only non-owned types have an independent Couchbase keyspace to write to.
            var currentEntry = (InternalEntityEntry)ownedEntry;
            InternalEntityEntry? rootInternalEntry = null;
            IEntityType? rootEntityType = null;

            while (true)
            {
                var ownership = currentEntry.EntityType.FindOwnership();
                if (ownership == null) break;

                var fkValues = ownership.Properties
                    .Select(p => currentEntry.GetCurrentValue(p))
                    .ToArray();

                var ownerEntry = currentEntry.StateManager.TryGetEntry(ownership.PrincipalKey, fkValues);
                if (ownerEntry == null) break;

                if (!ownership.PrincipalEntityType.IsOwned())
                {
                    rootInternalEntry = ownerEntry;
                    rootEntityType = ownership.PrincipalEntityType;
                    break;
                }

                currentEntry = ownerEntry;
            }

            if (rootInternalEntry == null || rootEntityType == null) continue;

            var ownerEntity = rootInternalEntry.Entity;
            var ownerPrimaryKey = rootEntityType.GetPrimaryKey(ownerEntity);
            var rootKey = $"{rootEntityType.ClrType.Name}:{ownerPrimaryKey}";

            if (writtenRoots.Contains(rootKey)) continue;
            writtenRoots.Add(rootKey);

            var ownerKeyspace = rootEntityType.GetCollectionName()
                ?? throw new InvalidOperationException(
                    $"Owner entity type '{rootEntityType.ClrType.Name}' has no mapped table name. " +
                    "Ensure the entity is mapped to a Couchbase collection via ToCouchbaseCollection().");

            var ownerDocument = HydrateObjectFromEntity(rootInternalEntry, _fieldNamingPolicy);
            pendingWrites.Add(new PendingWrite(CouchbaseWriteKind.Upsert, ownerPrimaryKey, ownerKeyspace, ownerDocument));
        }

        return await ExecutePendingWritesAsync(pendingWrites, transaction, cancellationToken).ConfigureAwait(false);
    }

    private enum CouchbaseWriteKind { Insert, Upsert, Delete }

    private readonly record struct PendingWrite(
        CouchbaseWriteKind Kind, string Key, string Keyspace, object? Document);

    // Upper bound on concurrent KV writes for a single SaveChanges so a very large change set
    // doesn't flood the SDK's connection pool. KV ops are I/O-bound, so this is well above core count.
    private const int MaxWriteConcurrency = 32;

    /// <summary>
    /// Dispatches the collected writes. Transactional writes are enqueued sequentially (single,
    /// ordered transaction); non-transactional writes target independent documents and are run
    /// concurrently (bounded) — Couchbase KV throughput is round-trip-bound, so serial awaits would
    /// make SaveChanges latency scale with the number of changed entities.
    /// </summary>
    private async Task<int> ExecutePendingWritesAsync(
        IReadOnlyList<PendingWrite> pendingWrites,
        CouchbaseDbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (pendingWrites.Count == 0)
        {
            return 0;
        }

        if (transaction != null)
        {
            var transactionalCount = 0;
            foreach (var write in pendingWrites)
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (write.Kind)
                {
                    case CouchbaseWriteKind.Delete:
                        await _couchbaseClient.EnqueueTransactionalRemove(transaction, write.Key, write.Keyspace).ConfigureAwait(false);
                        break;
                    case CouchbaseWriteKind.Upsert:
                        await _couchbaseClient.EnqueueTransactionalUpsert(transaction, write.Key, write.Keyspace, write.Document!).ConfigureAwait(false);
                        break;
                    case CouchbaseWriteKind.Insert:
                        await _couchbaseClient.EnqueueTransactionalInsert(transaction, write.Key, write.Keyspace, write.Document!).ConfigureAwait(false);
                        break;
                }
                transactionalCount++;
            }
            return transactionalCount;
        }

        // Non-atomic by design: like the original serial path, a non-transactional SaveChanges is
        // best-effort and may partially apply if a write fails. Because writes run concurrently, the
        // failure leaves a bounded, nondeterministic subset written (up to MaxWriteConcurrency
        // in-flight) rather than a deterministic prefix — Parallel.ForEachAsync stops scheduling new
        // writes on the first exception, but in-flight writes have already been sent. Use a Couchbase
        // transaction (the sequential branch above) when all-or-nothing semantics are required.
        // The external CancellationToken is honored at the scheduling level via ParallelOptions; the
        // individual KV calls are not yet cancellable (ICouchbaseClientWrapper takes no token).
        var successCount = 0;
        await Parallel.ForEachAsync(
            pendingWrites,
            new ParallelOptions { MaxDegreeOfParallelism = MaxWriteConcurrency, CancellationToken = cancellationToken },
            async (write, _) =>
            {
                var written = write.Kind switch
                {
                    CouchbaseWriteKind.Delete => await _couchbaseClient.DeleteDocument(write.Key, write.Keyspace).ConfigureAwait(false),
                    CouchbaseWriteKind.Upsert => await _couchbaseClient.UpdateDocument(write.Key, write.Keyspace, write.Document!).ConfigureAwait(false),
                    CouchbaseWriteKind.Insert => await _couchbaseClient.CreateDocument(write.Key, write.Keyspace, write.Document!).ConfigureAwait(false),
                    _ => false
                };
                if (written)
                {
                    Interlocked.Increment(ref successCount);
                }
            }).ConfigureAwait(false);
        return successCount;
    }

    // Internal seam so the null-nav behaviour can be verified without a live DbContext.
    internal static void FillOwnsOneIntoDoc(Dictionary<string, object?> doc, INavigation nav, object? navValue)
    {
        foreach (var p in nav.TargetEntityType.GetProperties().Where(p => !p.IsShadowProperty()))
        {
            // When the entire navigation is null every flattened column must be written as
            // null unconditionally — do NOT call ApplyConverter here.  A converter with
            // ConvertsNulls=true would map null to a non-null sentinel value; EF would then
            // see that non-null value on the read path and incorrectly treat the navigation
            // as present, causing materialisation to fail or return a phantom owned object.
            if (navValue == null)
            {
                doc[p.GetColumnName()] = null;
                continue;
            }

            // Read via PropertyInfo when available; fall back to FieldInfo for field-access properties.
            var rawValue = p.PropertyInfo != null ? p.PropertyInfo.GetValue(navValue)
                : p.FieldInfo?.GetValue(navValue);
            doc[p.GetColumnName()] = ApplyConverter(p, rawValue);
        }
    }

    private CouchbaseDbTransaction? GetCurrentTransaction()
    {
        if (_relationalConnection is CouchbaseRelationalConnection couchbaseRelationalConnection)
        {
            return couchbaseRelationalConnection.CouchbaseTransaction;
        }
        return null;
    }

    internal static object HydrateObjectFromEntity(IUpdateEntry updateEntry, JsonNamingPolicy? fieldNamingPolicy = null)
    {
        var entityType = updateEntry.EntityType;

        // For TPH inheritance the discriminator is a shadow property by default. It must
        // still be persisted so the query path (e.g. OfType<TDerived>() / Include on a
        // derived navigation) can filter documents to the correct CLR type.
        var discriminator = entityType.FindDiscriminatorProperty();

        var ownedNavs = entityType.GetNavigations()
            .Where(n => n.TargetEntityType.IsOwned() && n.PropertyInfo != null)
            .ToList();

        // No owned navigations: build a dictionary keyed by GetColumnName() so that
        // (a) value converters (HasConversion) can be applied before storage and
        // (b) the stored keys match what the N1QL SELECT projection aliases expect.
        //
        // Exception: shared entity types (HasSharedClrType == true, e.g. the hidden join
        // entity for skip navigations / HasMany().WithMany()) use Dictionary<string,object>
        // as their CLR type.  All their FK properties are shadow properties — the standard
        // IsShadowProperty filter would produce an empty document.  For shared types we
        // therefore write every property value (including shadow) by column name.
        if (ownedNavs.Count == 0)
        {
            if (entityType.HasSharedClrType)
            {
                // Shared entity type (skip-navigation join table): all FK columns are shadow
                // properties — write every property value by column name.
                var joinDoc = new Dictionary<string, object?>();
                foreach (var property in entityType.GetProperties())
                {
                    // Use GetColumnName() verbatim — do NOT apply fieldNamingPolicy here.
                    // The SQL generation path projects these columns by their exact column
                    // name (e.g. "PostsPostId", "TagsTagId"), so the written document keys
                    // must match. Applying a naming policy would silently diverge from the
                    // SQL projection and make join documents unreadable on the query path.
                    var rawJoinValue = updateEntry.GetCurrentValue(property);
                    joinDoc[property.GetColumnName()] = ApplyConverter(property, rawJoinValue);
                }
                return joinDoc;
            }

            // Regular entity type: build a dictionary with converter-applied values so that
            // types with HasConversion (e.g. enum → string) store the provider representation.
            var regularDoc = new Dictionary<string, object?>();
            foreach (var property in entityType.GetProperties())
            {
                if (property.IsShadowProperty() && property != discriminator) continue;
                var rawValue = updateEntry.GetCurrentValue(property);
                regularDoc[property.GetColumnName()] = ApplyConverter(property, rawValue);
            }
            return regularDoc;
        }

        // With owned navigations, build a flat dictionary so that:
        //   OwnsOne  → each owned scalar is stored as a top-level field whose key matches
        //              EF Core's projected column name (e.g. "address_street"), allowing the
        //              EF Core shaper to read it directly from the N1QL result row.
        //   OwnsMany → the collection is stored under its camelCase navigation name
        //              (e.g. "contactMethods") so AddOwnedCollectionColumnsToProjection
        //              (CouchbaseShapedQueryCompilingExpressionVisitor) can inject a
        //              ColumnExpression into the IR projection, and PopulateCollectionNavigations
        //              can look up the same key in the N1QL result row.
        // Dictionary keys are serialized verbatim by System.Text.Json — no camelCase
        // transformation — so GetColumnName() keys match what N1QL SELECT returns.
        //
        // Note: a custom JsonConverter<TOwnedItem> registered in the SDK's serializer options
        // will not fire for owned-item dictionaries, because the item-type boundary is erased.
        // The SDK sees Dictionary<string,object?> rather than TOwnedItem and dispatches on
        // the runtime type of each property value instead.
        var doc = new Dictionary<string, object?>();
        foreach (var property in entityType.GetProperties())
        {
            if (property.IsShadowProperty() && property != discriminator) continue;
            var rawDocValue = updateEntry.GetCurrentValue(property);
            doc[property.GetColumnName()] = ApplyConverter(property, rawDocValue);
        }

        var entity = updateEntry.ToEntityEntry().Entity;
        foreach (var nav in ownedNavs)
        {
            // Read via PropertyInfo when available; fall back to FieldInfo for field-access
            // properties (backing-field or [BackingField]-annotated) where PropertyInfo is null.
            var navValue = nav.PropertyInfo != null
                ? nav.PropertyInfo.GetValue(entity)
                : nav.FieldInfo?.GetValue(entity);
            if (nav.IsCollection)
            {
                var fieldName = fieldNamingPolicy?.ConvertName(nav.Name) ?? nav.Name;
                if (navValue is IEnumerable items)
                {
                    // Use SerializeOwnedItem so that nested OwnsOne / OwnsMany navigations
                    // within each item (e.g. ContactMethod.Label, ContactMethod.Tags) are
                    // recursively included.  The flat scalar-only loop it replaces silently
                    // dropped all nested navigations.
                    var list = new List<Dictionary<string, object?>>();
                    foreach (var item in items)
                        list.Add(SerializeOwnedItem(item, nav.TargetEntityType, fieldNamingPolicy));
                    doc[fieldName] = list;
                }
                else
                {
                    doc[fieldName] = null;
                }
            }
            else
            {
                FillOwnsOneIntoDoc(doc, nav, navValue);
            }
        }

        return doc;
    }

    /// <summary>
    /// Applies the EF Core value converter (<c>HasConversion</c>) for a property to produce
    /// the provider/storage representation before writing to Couchbase.  When no converter
    /// is configured the raw model-side value is returned unchanged.
    /// </summary>
    private static object? ApplyConverter(IProperty property, object? rawValue)
    {
        var converter = property.GetValueConverter() ?? property.FindTypeMapping()?.Converter;
        // Always call the converter if one is present, even for null rawValue, because a
        // converter with ConvertsNulls=true may map null to a non-null provider value.
        return converter is not null && (rawValue is not null || converter.ConvertsNulls)
            ? converter.ConvertToProvider(rawValue)
            : rawValue;
    }

    /// <summary>
    /// Recursively serializes a single owned-item CLR instance to a dictionary,
    /// including any nested OwnsOne / OwnsMany navigations at arbitrary depth.
    /// Internal so the field-access fallback can be verified without a live DbContext.
    /// </summary>
    internal static Dictionary<string, object?> SerializeOwnedItem(
        object item,
        IEntityType entityType,
        JsonNamingPolicy? fieldNamingPolicy)
    {
        var doc = new Dictionary<string, object?>();

        foreach (var p in entityType.GetProperties())
        {
            if (p.IsShadowProperty()) continue;
            // Read via PropertyInfo → FieldInfo fallback (field-access support).
            var rawValue = p.PropertyInfo != null
                ? p.PropertyInfo.GetValue(item)
                : p.FieldInfo?.GetValue(item);
            doc[fieldNamingPolicy?.ConvertName(p.Name) ?? p.Name] = ApplyConverter(p, rawValue);
        }

        foreach (var nav in entityType.GetNavigations())
        {
            // Skip only when the navigation is not owned or cannot be read at all.
            // Read via PropertyInfo when available; fall back to FieldInfo for field-access
            // navigations (backing-field or [BackingField]-annotated) where PropertyInfo is null.
            if (!nav.TargetEntityType.IsOwned() || (nav.PropertyInfo == null && nav.FieldInfo == null)) continue;
            var navValue = nav.PropertyInfo != null
                ? nav.PropertyInfo.GetValue(item)
                : nav.FieldInfo?.GetValue(item);
            var fieldName = fieldNamingPolicy?.ConvertName(nav.Name) ?? nav.Name;

            if (nav.IsCollection)
            {
                if (navValue is IEnumerable nested)
                {
                    var list = new List<Dictionary<string, object?>>();
                    foreach (var nestedItem in nested)
                        list.Add(SerializeOwnedItem(nestedItem, nav.TargetEntityType, fieldNamingPolicy));
                    doc[fieldName] = list;
                }
                else
                {
                    doc[fieldName] = null;
                }
            }
            else
            {
                if (navValue == null)
                {
                    doc[fieldName] = null;
                }
                else
                {
                    doc[fieldName] = SerializeOwnedItem(navValue, nav.TargetEntityType, fieldNamingPolicy);
                }
            }
        }

        return doc;
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
