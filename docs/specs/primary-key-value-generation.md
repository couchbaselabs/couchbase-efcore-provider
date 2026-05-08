# Couchbase EF Core Primary Key Value Generation Specification

**Status:** Complete (Phase 1-3)  
**Created:** 2026-05-05  
**Updated:** 2026-05-07  
**Phases:** 1-3 (Complete), 4 (Deferred)

---

## Overview

Implement automatic primary key value generation using Couchbase sequences, aligning with EF Core's `ValueGeneratedOnAdd` pattern.

## Couchbase SEQUENCE Reference

Couchbase Server 7.0+ supports sequences via SQL++:

```sql
CREATE SEQUENCE myBucket.myScope.mySequence START WITH 1 INCREMENT BY 1;
SELECT NEXT VALUE FOR myBucket.myScope.mySequence;
```

Documentation: https://docs.couchbase.com/server/current/n1ql/n1ql-language-reference/createsequence.html

---

## Phase 1: Core Infrastructure ✅ COMPLETE

### 1.1 `CouchbaseSequenceValueGenerator<T>`

**Location:** `src/Couchbase.EntityFrameworkCore/ValueGeneration/CouchbaseSequenceValueGenerator.cs`

- Generic value generator supporting `int`, `long`, `short`, `byte`, `uint`, `ulong`, `ushort`, `decimal`
- Executes `SELECT NEXT VALUE FOR bucket.scope.sequence_name`
- Uses `BuildSequenceQuery(ISqlGenerationHelper)` for centralized SQL generation with proper identifier escaping
- Gets `IRelationalConnection` from `EntityEntry.Context` at runtime (avoids DI lifetime issues)

### 1.2 `CouchbaseValueGeneratorSelector`

**Location:** `src/Couchbase.EntityFrameworkCore/ValueGeneration/CouchbaseValueGeneratorSelector.cs`

- Extends `RelationalValueGeneratorSelector`
- Checks for `Couchbase:SequenceName` annotation on properties
- Creates appropriate generic `CouchbaseSequenceValueGenerator<T>` via reflection
- Supports optional `Couchbase:SequenceScope` annotation for scope override
- Defines annotation keys: `SequenceNameAnnotation`, `SequenceScopeAnnotation`, `SequenceOptionsAnnotation`, `SequenceAutoCreateAnnotation`

### 1.3 DI Registration

- `IValueGeneratorSelector` registered in `CouchbaseServiceCollectionExtensions`
- Registered AFTER `TryAddCoreServices()` to override the default selector
- Existing selector is explicitly removed before adding ours

### 1.4 Save Pipeline Integration

- `CouchbaseSaveChangesInterceptor.GenerateSequenceValuesAsync()` generates values during `SavingChangesAsync`
- Detects properties with sequence annotations and temporary/default values
- Calls `IValueGeneratorSelector.Select()` to get generator, then `NextAsync()` to generate value
- Uses `propertyEntry.IsTemporary` to detect EF Core's temporary negative values

---

## Phase 2: Fluent API & Annotations ✅ COMPLETE

### 2.1 Extension Method: `UseSequence()`

**Location:** `src/Couchbase.EntityFrameworkCore/Extensions/CouchbasePropertyBuilderExtensions.cs`

```csharp
// Usage
modelBuilder.Entity<Order>()
    .Property(e => e.Id)
    .UseSequence("order_seq");  // Uses default scope
    
modelBuilder.Entity<Order>()
    .Property(e => e.Id)
    .UseSequence("analytics", "order_seq");  // Custom scope
```

**Important:** All overloads clear any previously set auto-create annotations to ensure consistent behavior. No-options overloads also clear scope and options annotations.

### 2.2 Data Annotation: `[CouchbaseSequence]`

**Location:** `src/Couchbase.EntityFrameworkCore/Metadata/CouchbaseSequenceAttribute.cs`

```csharp
public class Order
{
    [CouchbaseSequence("order_seq")]
    public long Id { get; set; }
}

// With options
public class OrderWithOptions
{
    [CouchbaseSequence("order_seq", StartWith = 1000, IncrementBy = 10, AutoCreate = true)]
    public long Id { get; set; }
}
```

**Properties:**
- `SequenceName` - Required sequence name
- `Scope` - Optional scope override (null = use DbContext scope)
- `StartWith` - Starting value (default: 1)
- `IncrementBy` - Increment value (default: 1)
- `Cycle` - Whether to restart at limit (default: false)
- `AutoCreate` - Auto-create on EnsureCreatedAsync (default: true)

**Note:** Sequences targeting a non-default scope (via `Scope` property) will not be auto-created even if `AutoCreate = true`, as the scope may not exist.

### 2.3 Convention: `CouchbaseSequenceConvention`

**Location:** `src/Couchbase.EntityFrameworkCore/Metadata/Conventions/CouchbaseSequenceConvention.cs`

- Processes `CouchbaseSequenceAttribute` during model building
- Sets annotations based on attribute configuration
- Only stores `AutoCreate` annotation when `false` (true is default)
- Only stores options annotation when non-default values are specified

---

## Phase 3: Sequence Lifecycle Management ✅ COMPLETE

### 3.1 `CouchbaseSequenceOptions` Record

**Location:** `src/Couchbase.EntityFrameworkCore/ValueGeneration/CouchbaseSequenceOptions.cs`

```csharp
public sealed record CouchbaseSequenceOptions
{
    public long StartWith { get; init; } = 1;
    public long IncrementBy { get; init; } = 1;
    public long? MaxValue { get; init; }
    public long? MinValue { get; init; }
    public bool Cycle { get; init; } = false;
    public int? CacheSize { get; init; }
    
    public string ToSqlOptionsClause() { ... }  // Public method for SQL generation
}
```

- Generates SQL++ `CREATE SEQUENCE` options clause via `ToSqlOptionsClause()`
- Supports all Couchbase sequence options: START WITH, INCREMENT BY, MAXVALUE, MINVALUE, CYCLE, CACHE
- `ToSqlOptionsClause()` is public for testability and reuse

### 3.2 Fluent API with Options

```csharp
modelBuilder.Entity<Order>()
    .Property(e => e.Id)
    .UseSequence("order_seq", new CouchbaseSequenceOptions
    {
        StartWith = 1000,
        IncrementBy = 10
    });
```

### 3.3 Attribute with Options

```csharp
public class Order
{
    [CouchbaseSequence("order_seq", StartWith = 1000, IncrementBy = 10)]
    public long Id { get; set; }
}
```

### 3.4 Auto-Creation on EnsureCreatedAsync

- `CouchbaseDatabaseCreator.CreateSequencesAsync()` scans model for sequence annotations
- Executes `CREATE SEQUENCE IF NOT EXISTS bucket.scope.sequence_name {options}`
- Called automatically during `EnsureCreatedAsync()` (always, even if bucket existed)
- Uses `ISqlGenerationHelper.DelimitIdentifier()` for proper identifier escaping
- Detects and throws `InvalidOperationException` for conflicting sequence options across properties
- Skips sequences with `AutoCreate = false` (logs at Debug level)
- Skips sequences targeting non-default scopes with warning (scope may not exist)

### 3.5 Sequence Drop on DeleteAsync

- `CouchbaseDatabaseCreator.DropSequencesAsync()` drops all model sequences
- Executes `DROP SEQUENCE IF EXISTS bucket.scope.sequence_name`
- Called during `DeleteAsync()` before bucket deletion
- Exceptions are suppressed with warning log (cleanup should not fail delete)

### 3.6 AutoCreateScopes Option

**Location:** `src/Couchbase.EntityFrameworkCore/Infrastructure/CouchbaseDbContextOptionsBuilder.cs`

```csharp
optionsBuilder.UseCouchbase(clusterOptions, opts =>
{
    opts.Bucket = "myBucket";
    opts.Scope = "myScope";
    opts.AutoCreateScopes = true;  // Enable auto-creation of non-default scopes
});
```

- When `true`, scopes referenced in entity keyspace mappings are created automatically
- When `false` (default), collections in non-default scopes are skipped with a warning
- Affects both collection and sequence creation

### 3.7 CouchbaseSqlGenerationHelper Security Fix

**Location:** `src/Couchbase.EntityFrameworkCore/Storage/Internal/CouchbaseSqlGenerationHelper.cs`

- Added `EscapeIdentifier()` overrides to properly escape backticks by doubling them
- Prevents SQL++ injection via malicious identifier names
- Example: `` my`name `` → `` `my``name` ``

### 3.8 CouchbaseDatabaseCreator Improvements

- `InitializeAsync()` is now idempotent (avoids double-initialization)
- `GetBucketAsync()` has retry limits (10 retries, 500ms delay) instead of infinite loop
- Fixed bug: `CreateScopeAsync()` was checking Bucket name instead of Scope name
- Proper logging throughout
- Removed unused methods `ScopeExistsAsync()` and `CollectionsExistsAsync()` (dead code with brittle logic)

---

## Phase 4: Client-Side GUID Support 📋 DEFERRED

### 4.1 Built-in GUID Generator

- Use EF Core's existing `GuidValueGenerator`
- Add `UseGuidKeys()` convenience method

### 4.2 ULID Support (Optional)

- Add `UseUlidKeys()` for time-sortable unique IDs
- Requires `Ulid` NuGet package

---

## Files Created/Modified

### Phase 1-3 (Complete)

| File | Action | Description |
|------|--------|-------------|
| `ValueGeneration/CouchbaseSequenceValueGenerator.cs` | Created | Generic value generator using `BuildSequenceQuery(ISqlGenerationHelper)` |
| `ValueGeneration/CouchbaseValueGeneratorSelector.cs` | Created | Selects generators, defines annotation keys |
| `ValueGeneration/CouchbaseSequenceOptions.cs` | Created | Sequence options with public `ToSqlOptionsClause()` |
| `Metadata/CouchbaseSequenceAttribute.cs` | Created | Data annotation with `StartWith`, `IncrementBy`, `Cycle`, `AutoCreate` |
| `Extensions/CouchbasePropertyBuilderExtensions.cs` | Created | `UseSequence()` fluent API (all overloads clear auto-create annotation) |
| `Metadata/Conventions/CouchbaseSequenceConvention.cs` | Created | Processes attributes, stores options/auto-create annotations |
| `Extensions/CouchbaseServiceCollectionExtensions.cs` | Modified | Registers selector after `TryAddCoreServices()` |
| `Storage/Internal/CouchbaseSaveChangesInterceptor.cs` | Modified | Generates sequence values using `IsTemporary` check |
| `Storage/Internal/CouchbaseDatabaseCreator.cs` | Modified | Sequence lifecycle, idempotent init, retry limits, scope fix |
| `Storage/Internal/CouchbaseSqlGenerationHelper.cs` | Modified | `EscapeIdentifier()` for backtick escaping (security) |
| `Infrastructure/CouchbaseDbContextOptionsBuilder.cs` | Modified | Added `AutoCreateScopes` property |
| `Infrastructure/ICouchbaseDbContextOptionsBuilder.cs` | Modified | Added `AutoCreateScopes` to interface |

### Phase 4 (Deferred)

| File | Action |
|------|--------|
| `Extensions/CouchbasePropertyBuilderExtensions.cs` | Extend with GUID/ULID support |

---

## Test Coverage

### Unit Tests (Phase 1-3) ✅

| Test File | Tests | Description |
|-----------|-------|-------------|
| `CouchbaseSequenceValueGeneratorTests.cs` | 12 | Constructor validation, type support, query generation with `ISqlGenerationHelper` |
| `CouchbaseSequenceAttributeTests.cs` | 17 | Attribute construction, properties, defaults |
| `CouchbasePropertyBuilderExtensionsTests.cs` | 21 | Fluent API, scope override, options clearing behavior |
| `CouchbaseSequenceOptionsTests.cs` | 13 | Options record, SQL clause generation |
| `CouchbaseDatabaseCreatorTests.cs` | 15 | Bucket/Scope usage, idempotent init, AutoCreateScopes |
| `CouchbaseSqlGenerationHelperTests.cs` | 15 | Backtick escaping, parameter naming |
| `CouchbaseDbContextOptionsBuilderTests.cs` | 8 | AutoCreateScopes, Bucket, Scope properties |
| `BoolAnnotationReadingTests.cs` | 5 | Boxed bool annotation pattern matching |

**Total Unit Tests:** 450

### Integration Tests (Phase 1-3) ✅

| Test File | Tests | Description |
|-----------|-------|-------------|
| `SequenceValueGenerationTests.cs` | 7 | End-to-end DI/model/save pipeline, options verification |

Key integration tests:
- `EndToEnd_DIRegistration_SelectorUsedDuringSaveChanges` - Full pipeline verification
- `UseSequence_WithOptions_SetsAnnotationCorrectly` - Options annotation verification
- `EnsureCreatedAsync_AutoCreatesSequence_WithOptions` - Sequence auto-creation

### Key Test: Bucket/Scope Confusion Bug

`CouchbaseDatabaseCreatorTests.EnsureCreatedAsync_ChecksForCorrectScopeNotBucket` specifically tests the scenario where bucket name matches a scope name but the configured scope doesn't exist. This would have caught the original bug where `CreateScopeAsync()` was checking `Bucket` instead of `Scope`.

### Integration Tests (Phase 4, Deferred)

- GUID/ULID key generation

---

## Constraints

- Requires Couchbase Server 7.0+
- Sequences are scoped to bucket.scope
- Supports numeric types: `int`, `long`, `short`, `byte`, `uint`, `ulong`, `ushort`, `decimal`
- Sequences are auto-created during `EnsureCreatedAsync()` with configured options
- Sequences targeting non-default scopes are not auto-created (scope may not exist)
- `AutoCreateScopes` must be enabled to auto-create collections in non-default scopes

---

## Bug Fixes During Implementation

| Bug | Fix | Test Coverage |
|-----|-----|---------------|
| `CreateScopeAsync()` checked Bucket instead of Scope | Changed to use `.Scope` | `EnsureCreatedAsync_ChecksForCorrectScopeNotBucket` |
| Backticks in identifiers not escaped (security) | Added `EscapeIdentifier()` to double backticks | `CouchbaseSqlGenerationHelperTests` (15 tests) |
| Boxed bool annotations read with `as bool?` (fails) | Changed to pattern matching `is bool b ? b : default` | `BoolAnnotationReadingTests` |
| `GetBucketAsync()` retried infinitely | Added max 10 retries with 500ms delay | N/A (infrastructure) |
| Double initialization in `DeleteAsync()` | Made `InitializeAsync()` idempotent | `MultipleOperations_InitializesClusterOnlyOnce` |
| `UseSequence` options overloads didn't clear auto-create | All overloads now clear `SequenceAutoCreateAnnotation` | N/A (consistency) |
| Unused methods with brittle logic | Removed `ScopeExistsAsync()` and `CollectionsExistsAsync()` | N/A (dead code removal) |

---

## Time Estimates (with Droid)

| Phase | Estimate | Actual |
|-------|----------|--------|
| Phase 1: Core Infrastructure | 2-4 hours | ~3 hours |
| Phase 2: Fluent API & Annotations | 1-2 hours | ~1 hour |
| Phase 3: Sequence Lifecycle | 1-2 hours | ~2 hours |
| Phase 4: Client-Side GUID | 30 min | Deferred |
| Testing & Bug Fixes | 2-3 hours | ~3 hours |

**Phase 1-3 Total: ~9 hours**
