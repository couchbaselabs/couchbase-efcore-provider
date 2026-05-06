# Couchbase EF Core Primary Key Value Generation Specification

**Status:** Complete (Phase 1-2)  
**Created:** 2026-05-05  
**Updated:** 2026-05-06  
**Phases:** 1-2 (Complete), 3-4 (Deferred)

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

## Phase 3: Sequence Lifecycle Management 📋 DEFERRED

### 3.1 Update `CouchbaseDatabaseCreator.CreateTables()`

- Scan model for sequence configurations
- Execute `CREATE SEQUENCE IF NOT EXISTS bucket.scope.sequence_name`
- Support sequence options: `START WITH`, `INCREMENT BY`, `MAXVALUE`, `CYCLE`

### 3.2 Sequence Options Model

```csharp
public class CouchbaseSequenceOptions
{
    public long StartWith { get; set; } = 1;
    public long IncrementBy { get; set; } = 1;
    public long? MaxValue { get; set; }
    public bool Cycle { get; set; } = false;
}
```

### 3.3 Sequence Drop on Delete

- Add sequence cleanup in `CouchbaseDatabaseCreator.Delete()`

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

### Phase 1-2 (Complete)

| File | Action | Description |
|------|--------|-------------|
| `ValueGeneration/CouchbaseSequenceValueGenerator.cs` | Created | Generic value generator for sequences |
| `ValueGeneration/CouchbaseValueGeneratorSelector.cs` | Created | Selects generators based on property annotations |
| `Metadata/CouchbaseSequenceAttribute.cs` | Created | Data annotation for sequence configuration |
| `Extensions/CouchbasePropertyBuilderExtensions.cs` | Created | `UseSequence()` fluent API |
| `Metadata/Conventions/CouchbaseSequenceConvention.cs` | Created | Processes sequence attributes |
| `Extensions/CouchbaseServiceCollectionExtensions.cs` | Modified | Registers `CouchbaseValueGeneratorSelector` |
| `Storage/Internal/CouchbaseSaveChangesInterceptor.cs` | Modified | Generates sequence values during save |

### Phase 3-4 (Deferred)

| File | Action |
|------|--------|
| `ValueGeneration/CouchbaseSequenceOptions.cs` | Create |
| `Storage/Internal/CouchbaseDatabaseCreator.cs` | Modify |
| `Extensions/CouchbasePropertyBuilderExtensions.cs` | Extend |

---

## Test Coverage

### Unit Tests (Phase 1-2) ✅

| Test File | Tests | Description |
|-----------|-------|-------------|
| `CouchbaseSequenceValueGeneratorTests.cs` | 12 | Constructor validation, type support, query generation |
| `CouchbaseSequenceAttributeTests.cs` | 10 | Attribute construction and properties |
| `CouchbasePropertyBuilderExtensionsTests.cs` | 11 | Fluent API annotation setting, scope override |

### Integration Tests (Phase 1-2) ✅

| Test File | Tests | Description |
|-----------|-------|-------------|
| `SequenceValueGenerationTests.cs` | 6 | End-to-end DI/model/save pipeline verification |

Key integration test: `EndToEnd_DIRegistration_SelectorUsedDuringSaveChanges` verifies:
1. `IValueGeneratorSelector` resolves to `CouchbaseValueGeneratorSelector`
2. Property has sequence annotation and `ValueGenerated.OnAdd`
3. Selector creates `CouchbaseSequenceValueGenerator<T>`
4. `SaveChangesAsync` assigns positive sequence value to entity

### Integration Tests (Phase 3-4, Deferred)

- Sequence auto-creation on EnsureCreated
- Batch insert pre-fetches multiple values
- Sequence options applied correctly

---

## Constraints

- Requires Couchbase Server 7.0+
- Sequences are scoped to bucket.scope
- Supports numeric types: `int`, `long`, `short`, `byte`, `uint`, `ulong`, `ushort`, `decimal`
- Sequences must be created manually before use (Phase 3 will add auto-creation)

---

## Time Estimates (with Droid)

| Phase | Estimate | Actual |
|-------|----------|--------|
| Phase 1: Core Infrastructure | 2-4 hours | ~3 hours |
| Phase 2: Fluent API & Annotations | 1-2 hours | ~1 hour |
| Phase 3: Sequence Lifecycle | 1-2 hours | Deferred |
| Phase 4: Client-Side GUID | 30 min | Deferred |
| Testing | 2-3 hours | ~2 hours |

**Phase 1-2 Total: ~6 hours**
