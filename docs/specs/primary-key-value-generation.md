# Couchbase EF Core Primary Key Value Generation Specification

**Status:** Complete (Phase 1-3)
**Created:** 2026-05-05  
**Updated:** 2026-05-06  
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
- Executes `SELECT NEXT VALUE FOR \`bucket\`.\`scope\`.\`sequence_name\``
- Gets `IRelationalConnection` from `EntityEntry.Context` at runtime (avoids DI lifetime issues)
- Properly escapes bucket, scope, and sequence names with backticks

### 1.2 `CouchbaseValueGeneratorSelector`

**Location:** `src/Couchbase.EntityFrameworkCore/ValueGeneration/CouchbaseValueGeneratorSelector.cs`

- Extends `RelationalValueGeneratorSelector`
- Checks for `Couchbase:SequenceName` annotation on properties
- Creates appropriate generic `CouchbaseSequenceValueGenerator<T>` via reflection
- Supports optional `Couchbase:SequenceScope` annotation for scope override

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

### 2.2 Data Annotation: `[CouchbaseSequence]`

**Location:** `src/Couchbase.EntityFrameworkCore/Metadata/CouchbaseSequenceAttribute.cs`

```csharp
public class Order
{
    [CouchbaseSequence("order_seq")]
    public long Id { get; set; }
}
```

### 2.3 Convention: `CouchbaseSequenceConvention`

**Location:** `src/Couchbase.EntityFrameworkCore/Metadata/Conventions/CouchbaseSequenceConvention.cs`

- Processes `CouchbaseSequenceAttribute` during model building
- Sets annotations based on attribute configuration

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
}
```

- Generates SQL++ `CREATE SEQUENCE` options clause via `ToSqlOptionsClause()`
- Supports all Couchbase sequence options: START WITH, INCREMENT BY, MAXVALUE, MINVALUE, CYCLE, CACHE

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
- Called automatically during `EnsureCreatedAsync()`

### 3.5 Sequence Drop on DeleteAsync

- `CouchbaseDatabaseCreator.DropSequencesAsync()` drops all model sequences
- Executes `DROP SEQUENCE IF EXISTS bucket.scope.sequence_name`
- Called during `DeleteAsync()` before bucket deletion

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
| `ValueGeneration/CouchbaseSequenceValueGenerator.cs` | Created | Generic value generator for sequences |
| `ValueGeneration/CouchbaseValueGeneratorSelector.cs` | Created | Selects generators based on property annotations |
| `ValueGeneration/CouchbaseSequenceOptions.cs` | Created | Sequence configuration options (Phase 3) |
| `Metadata/CouchbaseSequenceAttribute.cs` | Created | Data annotation with options support |
| `Extensions/CouchbasePropertyBuilderExtensions.cs` | Created | `UseSequence()` fluent API with options overloads |
| `Metadata/Conventions/CouchbaseSequenceConvention.cs` | Created | Processes sequence attributes with options |
| `Extensions/CouchbaseServiceCollectionExtensions.cs` | Modified | Registers `CouchbaseValueGeneratorSelector` |
| `Storage/Internal/CouchbaseSaveChangesInterceptor.cs` | Modified | Generates sequence values during save |
| `Storage/Internal/CouchbaseDatabaseCreator.cs` | Modified | Auto-creates/drops sequences (Phase 3) |

### Phase 4 (Deferred)

| File | Action |
|------|--------|
| `Extensions/CouchbasePropertyBuilderExtensions.cs` | Extend with GUID/ULID support |

---

## Test Coverage

### Unit Tests (Phase 1-3) ✅

| Test File | Tests | Description |
|-----------|-------|-------------|
| `CouchbaseSequenceValueGeneratorTests.cs` | 12 | Constructor validation, type support, query generation |
| `CouchbaseSequenceAttributeTests.cs` | 10 | Attribute construction and properties |
| `CouchbasePropertyBuilderExtensionsTests.cs` | 11 | Fluent API annotation setting, scope override |
| `CouchbaseSequenceOptionsTests.cs` | 13 | Options record, SQL clause generation (Phase 3) |

### Integration Tests (Phase 1-3) ✅

| Test File | Tests | Description |
|-----------|-------|-------------|
| `SequenceValueGenerationTests.cs` | 7 | End-to-end DI/model/save pipeline, options verification |

Key integration tests:
- `EndToEnd_DIRegistration_SelectorUsedDuringSaveChanges` - Full pipeline verification
- `UseSequence_WithOptions_SetsAnnotationCorrectly` - Options annotation verification

### Integration Tests (Phase 4, Deferred)

- GUID/ULID key generation

---

## Constraints

- Requires Couchbase Server 7.0+
- Sequences are scoped to bucket.scope
- Supports numeric types: `int`, `long`, `short`, `byte`, `uint`, `ulong`, `ushort`, `decimal`
- Sequences are auto-created during `EnsureCreatedAsync()` with configured options

---

## Time Estimates (with Droid)

| Phase | Estimate | Actual |
|-------|----------|--------|
| Phase 1: Core Infrastructure | 2-4 hours | ~3 hours |
| Phase 2: Fluent API & Annotations | 1-2 hours | ~1 hour |
| Phase 3: Sequence Lifecycle | 1-2 hours | ~1 hour |
| Phase 4: Client-Side GUID | 30 min | Deferred |
| Testing | 2-3 hours | ~2.5 hours |

**Phase 1-3 Total: ~7.5 hours**
