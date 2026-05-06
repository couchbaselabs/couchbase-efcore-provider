# Couchbase EF Core Primary Key Value Generation Specification

**Status:** In Progress  
**Created:** 2026-05-05  
**Phases:** 1-2 (Active), 3-4 (Deferred)

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

## Phase 1: Core Infrastructure ✅ ACTIVE

### 1.1 Create `CouchbaseSequenceValueGenerator`

**Location:** `src/Couchbase.EntityFrameworkCore/ValueGeneration/CouchbaseSequenceValueGenerator.cs`

- Implements `ValueGenerator<long>` 
- Executes `SELECT NEXT VALUE FOR bucket.scope.sequence_name`
- Caches sequence reference for performance

### 1.2 Create `CouchbaseSequenceValueGeneratorFactory`

**Location:** `src/Couchbase.EntityFrameworkCore/ValueGeneration/CouchbaseSequenceValueGeneratorFactory.cs`

- Implements `IValueGeneratorSelector`
- Creates appropriate generator based on property configuration

### 1.3 Register in DI

- Add `IValueGeneratorSelector` registration in `CouchbaseServiceCollectionExtensions`

---

## Phase 2: Fluent API & Annotations ✅ ACTIVE

### 2.1 Extension Method: `UseSequence()`

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

```csharp
public class Order
{
    [CouchbaseSequence("order_seq")]
    public long Id { get; set; }
}
```

### 2.3 Convention for Auto-Detection

- Properties named `Id` or `{EntityName}Id` of type `long`/`int` with `ValueGeneratedOnAdd` could auto-use sequences

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

## Files to Create/Modify

### Phase 1-2 (Active)

| File | Action |
|------|--------|
| `ValueGeneration/CouchbaseSequenceValueGenerator.cs` | Create |
| `ValueGeneration/CouchbaseSequenceValueGeneratorFactory.cs` | Create |
| `Metadata/CouchbaseSequenceAttribute.cs` | Create |
| `Extensions/CouchbasePropertyBuilderExtensions.cs` | Create |
| `Metadata/Conventions/CouchbaseSequenceConvention.cs` | Create |
| `Extensions/CouchbaseServiceCollectionExtensions.cs` | Modify |

### Phase 3-4 (Deferred)

| File | Action |
|------|--------|
| `ValueGeneration/CouchbaseSequenceOptions.cs` | Create |
| `Storage/Internal/CouchbaseDatabaseCreator.cs` | Modify |
| `Extensions/CouchbasePropertyBuilderExtensions.cs` | Extend |

---

## Test Coverage

### Unit Tests (Phase 1-2)

- Sequence value generator fetches values correctly
- Fluent API configures sequence properly
- Attribute is processed by convention
- Invalid configurations throw appropriate exceptions

### Integration Tests (Phase 1-2)

- End-to-end insert with sequence-generated ID
- Concurrent inserts get unique values

### Integration Tests (Phase 3-4, Deferred)

- Sequence auto-creation on EnsureCreated
- Batch insert pre-fetches multiple values
- Sequence options applied correctly

---

## Constraints

- Requires Couchbase Server 7.0+
- Sequences are scoped to bucket.scope
- Initial implementation supports `long` keys only
- Transaction support deferred to future iteration

---

## Time Estimates (with Droid)

| Phase | Estimate |
|-------|----------|
| Phase 1: Core Infrastructure | 2-4 hours |
| Phase 2: Fluent API & Annotations | 1-2 hours |
| Phase 3: Sequence Lifecycle | 1-2 hours |
| Phase 4: Client-Side GUID | 30 min |
| Testing | 2-3 hours |

**Phase 1-2 Total: ~4-6 hours**
