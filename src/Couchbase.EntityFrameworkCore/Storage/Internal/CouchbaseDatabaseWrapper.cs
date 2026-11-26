using System.Collections;
using Couchbase.EntityFrameworkCore.Diagnostics.Internal;
using System.Text.Json;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Internal;
using Couchbase.EntityFrameworkCore.Metadata;
using Couchbase.EntityFrameworkCore.Utils;
using Couchbase.EntityFrameworkCore.ValueGeneration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
    private readonly CouchbaseDocumentKeyFormat _documentKeyFormat;
    private readonly char _documentKeyDelimiter;
    private readonly CouchbaseMutationStateTracker _mutationStateTracker;
    private readonly string _bucket;
    private readonly string _scope;

    public CouchbaseDatabaseWrapper(
        DatabaseDependencies dependencies,
        ICouchbaseClientWrapper couchbaseClient,
        IRelationalConnection relationalConnection,
        ICouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder,
        CouchbaseMutationStateTracker mutationStateTracker)
        : base(dependencies)
    {
        _couchbaseClient = couchbaseClient ?? throw new ArgumentNullException(nameof(couchbaseClient));
        _relationalConnection = relationalConnection ?? throw new ArgumentNullException(nameof(relationalConnection));
        _fieldNamingPolicy = couchbaseDbContextOptionsBuilder.FieldNamingPolicy;
        _documentKeyFormat = couchbaseDbContextOptionsBuilder.DocumentKeyFormat;
        _documentKeyDelimiter = couchbaseDbContextOptionsBuilder.DocumentKeyDelimiter;
        _bucket = couchbaseDbContextOptionsBuilder.Bucket;
        _scope = couchbaseDbContextOptionsBuilder.Scope;
        _mutationStateTracker = mutationStateTracker ?? throw new ArgumentNullException(nameof(mutationStateTracker));
    }

    /// <summary>
    /// The document key for an entry, in the configured <see cref="CouchbaseDocumentKeyFormat"/>.
    /// Values always come from the update entry, which has store-generated values (such as sequence
    /// values) before EF Core copies them to the CLR object. Legacy keys keep their original part
    /// order (PK order for shared entity types, CLR property order for everything else).
    /// </summary>
    private string GetDocumentKey(IEntityType entityType, IUpdateEntry updateEntry, object entity)
    {
        if (_documentKeyFormat == CouchbaseDocumentKeyFormat.V2)
        {
            return CouchbaseDocumentKeyBuilder.BuildV2(entityType, updateEntry, _documentKeyDelimiter);
        }

        return entityType.HasSharedClrType
            ? entityType.GetPrimaryKey(updateEntry)
            : entityType.GetLegacyPrimaryKey(entity, updateEntry);
    }

    public override int SaveChanges(IList<IUpdateEntry> entries)
    {
        return SyncIo.Run(
            _relationalConnection.Context.GetService<IDiagnosticsLogger<DbLoggerCategory.Update>>().SyncNotSupported,
            static state => state.self.SaveChangesAsync(state.entries),
            (self: this, entries));
    }

    public override async Task<int> SaveChangesAsync(IList<IUpdateEntry> entries, CancellationToken cancellationToken = new())
    {
        // Before any document is built: a dependent's foreign key gets its principal's value.
        await GenerateSequenceValuesAsync(entries, cancellationToken).ConfigureAwait(false);
        AssignOwnedCollectionKeys(entries);

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
                // OwnsMany items are materialised via raw reflection in
                // CouchbaseOwnedCollectionMaterializer, bypassing EF Core's normal attach/fixup
                // path entirely — so they always come back tracked as Added regardless of
                // whether the user actually changed anything. EntityState is therefore not a
                // trustworthy "did this genuinely change" signal for a collection-owned (OwnsMany)
                // entry; skip it here and rely exclusively on the snapshot-based comparison in
                // CouchbaseSaveChangesInterceptor.MarkOwnersWithReplacedCollections (which already
                // marks the owner Modified for a real reference-replaced/added/removed/mutated
                // change) to avoid rewriting the owner on every untouched read+save.
                //
                // A reference-owned (OwnsOne) entry has no such artifact: it's a normal
                // table-split, fixup-tracked entity, so its EntityState IS a genuine signal —
                // keep deferring on it as before.
                var ownership = entityType.FindOwnership();
                if (ownership != null && !ownership.IsUnique)
                    continue;

                // Collect owned entries with actual changes; their owners will be written
                // in the second pass below if not already written by the main loop.
                if (updateEntry.EntityState is not (EntityState.Unchanged or EntityState.Detached))
                    deferredOwnedEntries.Add(updateEntry);
                continue;
            }

            // Shared entity types (e.g. hidden join table from HasMany().WithMany()) have
            // all PK columns as shadow properties — use the IUpdateEntry overload so the
            // values are read from the entry rather than the CLR Dictionary<string,object>.
            var primaryKey = GetDocumentKey(entityType, updateEntry, entity);
            var keyspace = entityType.GetCollectionName()
                ?? throw new InvalidOperationException(
                    $"Entity type '{entityType.ClrType.Name}' has no mapped table name. " +
                    "Ensure the entity is mapped to a Couchbase collection via ToCouchbaseCollection().");

            var casProperty = GetCasProperty(entityType);

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
                    pendingWrites.Add(new PendingWrite(CouchbaseWriteKind.Delete, primaryKey, keyspace, null,
                        updateEntry, null, GetCasCheckValue(casProperty, updateEntry)));
                    break;

                case EntityState.Modified:
                    writtenRoots.Add($"{entityType.ClrType.Name}:{primaryKey}");
                    pendingWrites.Add(new PendingWrite(CouchbaseWriteKind.Upsert, primaryKey, keyspace,
                        HydrateObjectFromEntity(updateEntry, _fieldNamingPolicy),
                        updateEntry, casProperty, GetCasCheckValue(casProperty, updateEntry)));
                    break;

                case EntityState.Added:
                    writtenRoots.Add($"{entityType.ClrType.Name}:{primaryKey}");
                    pendingWrites.Add(new PendingWrite(CouchbaseWriteKind.Insert, primaryKey, keyspace,
                        HydrateObjectFromEntity(updateEntry, _fieldNamingPolicy),
                        updateEntry, casProperty, null));
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
            var ownerPrimaryKey = GetDocumentKey(rootEntityType, rootInternalEntry, ownerEntity);
            var rootKey = $"{rootEntityType.ClrType.Name}:{ownerPrimaryKey}";

            if (writtenRoots.Contains(rootKey)) continue;
            writtenRoots.Add(rootKey);

            var ownerKeyspace = rootEntityType.GetCollectionName()
                ?? throw new InvalidOperationException(
                    $"Owner entity type '{rootEntityType.ClrType.Name}' has no mapped table name. " +
                    "Ensure the entity is mapped to a Couchbase collection via ToCouchbaseCollection().");

            var ownerDocument = HydrateObjectFromEntity(rootInternalEntry, _fieldNamingPolicy);
            var ownerCasProperty = GetCasProperty(rootEntityType);
            pendingWrites.Add(new PendingWrite(CouchbaseWriteKind.Upsert, ownerPrimaryKey, ownerKeyspace, ownerDocument,
                rootInternalEntry, ownerCasProperty, GetCasCheckValue(ownerCasProperty, rootInternalEntry)));
        }

        return await ExecutePendingWritesAsync(pendingWrites, transaction, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gives every added entry whose key uses a Couchbase sequence its value from the sequence, as
    /// a store-generated value, like an identity column: EF Core's checks before the save see the
    /// temporary value, the dependents' foreign keys get the new value, and a failed save discards
    /// it. Fetches all the values each sequence needs in one query. A key the application set
    /// itself (not temporary and not the CLR default) is kept.
    /// </summary>
    private async Task GenerateSequenceValuesAsync(IList<IUpdateEntry> entries, CancellationToken cancellationToken)
    {
        // Grouped by location only: the options only feed the error message for a missing
        // sequence (its CREATE SEQUENCE statement), so they come from the group's first property.
        Dictionary<CouchbaseSequenceLocation, (CouchbaseSequenceOptions Options, List<(IUpdateEntry, IProperty)> Keys)>? groups = null;

        foreach (var entry in entries)
        {
            if (entry.EntityState != EntityState.Added)
            {
                continue;
            }

            foreach (var property in entry.EntityType.GetProperties())
            {
                if (property.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation) is null
                    || (!entry.HasTemporaryValue(property) && !IsClrDefault(entry.GetCurrentValue(property), property.ClrType)))
                {
                    continue;
                }

                var location = CouchbaseSequenceLocation.Find(property, _bucket, _scope)!.Value;
                groups ??= [];
                if (!groups.TryGetValue(location, out var group))
                {
                    var options = property.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceOptionsAnnotation)?.Value
                        as CouchbaseSequenceOptions ?? CouchbaseSequenceOptions.Default;
                    groups[location] = group = (options, []);
                }

                group.Keys.Add((entry, property));
            }
        }

        if (groups is null)
        {
            return;
        }

        foreach (var (location, (options, keys)) in groups)
        {
            var values = await CouchbaseSequenceBatch
                .NextValuesAsync(_relationalConnection.Context, location, options, keys.Count, cancellationToken)
                .ConfigureAwait(false);

            for (var i = 0; i < keys.Count; i++)
            {
                var (entry, property) = keys[i];
                var clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                entry.SetStoreGeneratedValue(property, Convert.ChangeType(values[i], clrType));
            }
        }
    }

    /// <summary>
    /// Gives each added owned collection item with a temporary generated key (an <c>OwnsMany</c> item
    /// type with no key of its own) a number unique within its collection: the highest value already
    /// in the collection plus one. A key value the application set itself is kept.
    /// </summary>
    internal static void AssignOwnedCollectionKeys(IList<IUpdateEntry> entries)
    {
        HashSet<(InternalEntityEntry Owner, INavigation Navigation)>? assigned = null;

        // The order does not matter for nested collections: an item is found from its owner's key
        // as it is now, and when the owner's generated value is set, EF Core updates its items'
        // foreign keys.
        foreach (var entry in entries.Where(e => e.EntityState == EntityState.Added
                     && e.EntityType.FindOwnership() is { IsUnique: false }))
        {
            var ownership = entry.EntityType.FindOwnership()!;
            if (GeneratedKeyProperty(entry.EntityType, ownership) is not { } keyProperty
                || !entry.HasTemporaryValue(keyProperty)
                || ownership.PrincipalToDependent is not { } navigation
                || entry is not InternalEntityEntry itemEntry
                || itemEntry.StateManager.FindPrincipal(itemEntry, ownership) is not { } ownerEntry)
            {
                continue;
            }

            assigned ??= [];
            if (!assigned.Add((ownerEntry, navigation))
                || ownerEntry.GetCurrentValue(navigation) is not IEnumerable items)
            {
                continue;
            }

            var itemEntries = new List<InternalEntityEntry>();
            foreach (var item in items)
            {
                if (item != null && itemEntry.StateManager.TryGetEntry(item, entry.EntityType) is { } e)
                {
                    itemEntries.Add(e);
                }
            }

            var next = itemEntries
                .Where(e => !e.HasTemporaryValue(keyProperty))
                .Select(e => Convert.ToInt64(e.GetCurrentValue(keyProperty)))
                .DefaultIfEmpty(0)
                .Max() + 1;

            var clrType = Nullable.GetUnderlyingType(keyProperty.ClrType) ?? keyProperty.ClrType;
            foreach (var e in itemEntries.Where(e => e.HasTemporaryValue(keyProperty)))
            {
                var value = Convert.ChangeType(next++, clrType);
                if (keyProperty.IsShadowProperty())
                {
                    // Not in the document; a failed save discards it and a retry assigns it again.
                    e.SetStoreGeneratedValue(keyProperty, value);
                }
                else
                {
                    // The document is built from the CLR object, so set the value on the object now
                    // (this also clears the temporary flag). It stays after a failed save, so a retry
                    // keeps it.
                    e.SetProperty(keyProperty, value, isMaterialization: false, setModified: false);
                }
            }
        }
    }

    // The key property of an owned collection item that EF Core generates: the one key property that
    // is not part of the foreign key to the owner, if it is a generated int or long. The key must
    // contain the whole foreign key, because the values are only unique within one owner.
    private static IProperty? GeneratedKeyProperty(IEntityType itemType, IForeignKey ownership)
        => itemType.FindPrimaryKey() is { } key
           && ownership.Properties.All(key.Properties.Contains)
           && key.Properties.Where(p => !ownership.Properties.Contains(p)).ToList() is [var property]
           && property.ValueGenerated != ValueGenerated.Never
           && (Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) is var type
           && (type == typeof(int) || type == typeof(long))
            ? property
            : null;

    private static bool IsClrDefault(object? value, Type clrType)
        => value is null || (clrType.IsValueType && value.Equals(Activator.CreateInstance(clrType)));

    // A [CouchbaseMeta(Cas)]-annotated property, refreshed with the resulting CAS after every
    // successful insert/update regardless of whether it's also a concurrency token (harmless and
    // keeps the property useful for inspection even without concurrency enforcement).
    private static IProperty? GetCasProperty(IEntityType entityType)
        => entityType.GetProperties().FirstOrDefault(p =>
            p.FindAnnotation(CouchbaseMetaAnnotationNames.MetaField)?.Value as string == nameof(CouchbaseMetaField.Cas));

    // Only pass a CAS as an input check (enforcing optimistic concurrency) when the property is
    // ALSO marked .IsConcurrencyToken() -- required together deliberately, so adding
    // [CouchbaseMeta(Cas)] alone never silently starts throwing DbUpdateConcurrencyException.
    //
    // Uses GetOriginalValue, not GetCurrentValue: EF Core's documented DbUpdateConcurrencyException
    // resolution pattern is `entry.OriginalValues.SetValues(databaseValues)` before retrying
    // SaveChanges -- that call updates OriginalValues only, never CurrentValues. Reading
    // GetCurrentValue here would keep sending the stale, already-rejected CAS on every retry (an
    // infinite failure loop) even though the app followed EF Core's own recommended pattern
    // exactly. OriginalValues is also the semantically correct source for a concurrency check
    // regardless: it represents "what this entry believes is currently in the database," which is
    // exactly the value Couchbase's compare-and-swap needs to test against.
    private static ulong? GetCasCheckValue(IProperty? casProperty, IUpdateEntry updateEntry)
        => casProperty is { IsConcurrencyToken: true }
            ? (ulong)updateEntry.GetOriginalValue(casProperty)!
            : null;

    private enum CouchbaseWriteKind { Insert, Upsert, Delete }

    private readonly record struct PendingWrite(
        CouchbaseWriteKind Kind, string Key, string Keyspace, object? Document,
        IUpdateEntry? UpdateEntry, IProperty? CasProperty, ulong? Cas);

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
                        await _couchbaseClient.EnqueueTransactionalRemove(transaction, write.Key, write.Keyspace, cancellationToken).ConfigureAwait(false);
                        break;
                    case CouchbaseWriteKind.Upsert:
                        await _couchbaseClient.EnqueueTransactionalUpsert(transaction, write.Key, write.Keyspace, write.Document!, cancellationToken).ConfigureAwait(false);
                        break;
                    case CouchbaseWriteKind.Insert:
                        await _couchbaseClient.EnqueueTransactionalInsert(transaction, write.Key, write.Keyspace, write.Document!, cancellationToken).ConfigureAwait(false);
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
        // The external CancellationToken is honored both at the scheduling level (via ParallelOptions)
        // and per write (passed to each KV call). We pass the external token deliberately, not the
        // loop's combined token, so a sibling write's failure does not cancel in-flight writes — that
        // keeps the surfaced exception the original DbUpdateException rather than a cancellation.
        var successCount = 0;
        await Parallel.ForEachAsync(
            pendingWrites,
            new ParallelOptions { MaxDegreeOfParallelism = MaxWriteConcurrency, CancellationToken = cancellationToken },
            async (write, _) =>
            {
                try
                {
                    switch (write.Kind)
                    {
                        case CouchbaseWriteKind.Delete:
                            await _couchbaseClient.DeleteDocument(write.Key, write.Keyspace, write.Cas, cancellationToken).ConfigureAwait(false);
                            break;

                        case CouchbaseWriteKind.Upsert:
                        {
                            var result = await _couchbaseClient.UpdateDocument(write.Key, write.Keyspace, write.Document!, write.Cas, cancellationToken).ConfigureAwait(false);
                            RefreshCasProperty(write, result.Cas);
                            _mutationStateTracker.Add(result);
                            break;
                        }

                        case CouchbaseWriteKind.Insert:
                        {
                            var result = await _couchbaseClient.CreateDocument(write.Key, write.Keyspace, write.Document!, cancellationToken).ConfigureAwait(false);
                            RefreshCasProperty(write, result.Cas);
                            _mutationStateTracker.Add(result);
                            break;
                        }
                    }
                }
                catch (Couchbase.Core.Exceptions.CasMismatchException ex)
                {
                    // Translated to EF Core's own concurrency-conflict exception -- the same type
                    // applications already catch/retry against for any other provider, so CAS
                    // concurrency support needs no new app-facing exception-handling API.
                    throw new DbUpdateConcurrencyException(
                        $"The document '{write.Key}' in keyspace '{write.Keyspace}' was modified or " +
                        "deleted by another process since it was read (CAS mismatch).",
                        ex,
                        write.UpdateEntry != null ? [write.UpdateEntry] : []);
                }

                Interlocked.Increment(ref successCount);
            }).ConfigureAwait(false);
        return successCount;
    }

    // Writes the fresh, post-write CAS back onto the tracked entry so a subsequent SaveChanges
    // against the same entity has the correct up-to-date value to check against -- mirrors how
    // any other database-generated value (e.g. an identity column) is reflected back after a save.
    private static void RefreshCasProperty(PendingWrite write, ulong newCas)
    {
        if (write.CasProperty != null && write.UpdateEntry != null)
        {
            write.UpdateEntry.SetStoreGeneratedValue(write.CasProperty, newCas, setModified: false);
        }
    }

    // Internal seam so the null-nav behaviour can be verified without a live DbContext.
    internal static void FillOwnsOneIntoDoc(Dictionary<string, object?> doc, INavigation nav, object? navValue)
    {
        foreach (var p in nav.TargetEntityType.GetProperties().Where(p => !p.IsShadowProperty() && !IsMetaBacked(p)))
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

    // A [CouchbaseMeta]/HasCouchbaseMeta-annotated property (e.g. Cas, sourced from META()) has no
    // document-body field at all -- it must never be written, regardless of whether it happens to
    // be a shadow property or a real CLR property (the documented usage is a real property, e.g.
    // `public ulong Cas { get; set; }`, so IsShadowProperty() alone would not catch it).
    private static bool IsMetaBacked(IProperty property)
        => property.FindAnnotation(CouchbaseMetaAnnotationNames.MetaField) != null;

    internal static object HydrateObjectFromEntity(IUpdateEntry updateEntry, JsonNamingPolicy? fieldNamingPolicy = null)
    {
        var entityType = updateEntry.EntityType;

        // Every property of the entity is written, shadow properties included: the TPH discriminator,
        // scalar shadow properties and shadow foreign keys are all real data that the query path reads
        // back. Only META()-backed properties have no field in the document body.

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
                    if (IsMetaBacked(property)) continue;
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
                if (IsMetaBacked(property)) continue;
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
        //              (e.g. "contactMethods") so AddOwnedNavigationColumnsToProjection
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
            if (IsMetaBacked(property)) continue;
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
    internal static object? ApplyConverter(IProperty property, object? rawValue)
    {
        var explicitConverter = property.GetValueConverter();
        if (explicitConverter is null && property.GetElementType() is not null)
        {
            // Scalar "primitive collection" properties (List<T>/T[], not OwnsMany) get an
            // automatic CollectionToJsonStringConverter from EF Core's type-mapping source
            // unless a provider declares native array storage support -- correct for a
            // relational text column, but Couchbase documents are already JSON, so applying it
            // here would double-encode the array as a quoted JSON string instead of a native
            // array field. Skip it and let the raw CLR collection flow through to the
            // document's own JSON serialization, which already writes it as a real array (see
            // CouchbaseArrayIndexExpression/TranslatePrimitiveCollection, which assume exactly
            // this on the read side).
            return rawValue;
        }

        var converter = explicitConverter ?? property.FindTypeMapping()?.Converter;
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
            if (IsMetaBacked(p)) continue;
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
