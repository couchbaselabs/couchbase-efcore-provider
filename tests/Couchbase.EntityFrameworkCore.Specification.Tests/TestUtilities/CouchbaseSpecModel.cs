using Couchbase.EntityFrameworkCore.ValueGeneration;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;

/// <summary>Shared model adjustments every spec fixture needs on Couchbase.</summary>
public static class CouchbaseSpecModel
{
    public static string CollectionName(string name)
        => System.Text.RegularExpressions.Regex.Replace(name, "[^A-Za-z0-9_%-]", "_");

    public static void Apply(ModelBuilder modelBuilder, DbContext context, bool mapRowVersionsToCas = false)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // Couchbase collection names allow only A-Z, a-z, 0-9, _, - and %; EF derives names from
            // CLR types ("Order Details", "BoolOnlyKey<bool>"). Only bare names are touched.
            var table = entityType.GetTableName();
            if (table != null && !(table.Contains('.') && table.Split('.').Length == 3))
            {
                var safe = CollectionName(table);
                if (safe != table)
                {
                    entityType.SetTableName(safe);
                }
            }

            if (mapRowVersionsToCas)
            {
                foreach (var p in entityType.GetDeclaredProperties()
                             .Where(p => p.ClrType == typeof(ulong) && p.IsConcurrencyToken
                                 && p.ValueGenerated == ValueGenerated.OnAddOrUpdate))
                {
                    p.SetAnnotation(CouchbaseMetaAnnotationNames.MetaField, nameof(CouchbaseMetaField.Cas));
                }
            }
        }

        // Test data: the suites seed rows with explicit low keys (1, 2, 3, ...), and the provider
        // generates integer keys from a sequence (CouchbaseKeySequenceConvention) that would start
        // at 1 and collide with them. Start generated keys well above any seeded key. The
        // convention names the sequence when the model is finalized; this only sets its options.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.BaseType != null || entityType.FindPrimaryKey() is not { Properties: [var property] }
                || property.ValueGenerated != ValueGenerated.OnAdd || property.GetValueGeneratorFactory() != null)
            {
                continue;
            }

            var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            if (type == typeof(int) || type == typeof(long))
            {
                property.SetAnnotation(
                    CouchbaseValueGeneratorSelector.SequenceOptionsAnnotation,
                    new CouchbaseSequenceOptions { StartWith = 1_000_000 });
            }
        }

        modelBuilder.ConfigureToCouchbase(context);

        // Test-harness only: EMULATES a fix for ConfigureToCouchbase giving every derived type in a
        // TPH hierarchy its own collection (EF then rejects the model). Derived TPH types inherit
        // the root's keyspace again.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(e => e.BaseType != null))
        {
            var strategy = entityType.GetRootType().GetMappingStrategy() ?? RelationalAnnotationNames.TphMappingStrategy;
            if (strategy == RelationalAnnotationNames.TphMappingStrategy)
            {
                entityType.RemoveAnnotation(RelationalAnnotationNames.TableName);
            }
        }

        // Test-harness only: EMULATES the same fix for TPC. ConfigureToCouchbase also gives an
        // abstract TPC base type a collection, which EF rejects (only concrete types are stored).
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(e => e.ClrType.IsAbstract
                         && e.GetRootType().GetMappingStrategy() == RelationalAnnotationNames.TpcMappingStrategy))
        {
            entityType.SetTableName(null);
        }

        // Test-harness only: EMULATES a fix for JsonPropertyNameConvention, which turns
        // [JsonPropertyName] into a column name even on JSON-mapped (ToJson) owned types. EF maps the
        // attribute to the JSON property name itself and rejects a property that has both.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(e => e.IsMappedToJson()))
        {
            foreach (var property in entityType.GetDeclaredProperties())
            {
                if (property.FindAnnotation(RelationalAnnotationNames.ColumnName) != null
                    && property.FindAnnotation(RelationalAnnotationNames.JsonPropertyName) != null)
                {
                    property.RemoveAnnotation(RelationalAnnotationNames.ColumnName);
                }
            }
        }
    }
}

public static class CouchbaseSpecModelExtensions
{
    /// <summary>
    /// Gives each type its own collection, named after the type. The provider rejects table
    /// splitting (two entity types in one collection would write to the same document key). EF
    /// fixtures that use it call this so that the rest of their tests keep running. Call it before
    /// <see cref="CouchbaseSpecModel.Apply"/>, which qualifies the names.
    /// </summary>
    public static void MapToOwnCollections(this ModelBuilder modelBuilder, params Type[] types)
    {
        foreach (var type in types)
        {
            modelBuilder.Entity(type).ToTable(CouchbaseSpecModel.CollectionName(type.Name));
        }
    }

    /// <summary>
    /// Maps TPT hierarchies as TPH: the derived types go to the root's collection. The provider
    /// rejects TPT (queries join a base collection that the save path never writes to). EF fixtures
    /// that use it call this so that the rest of their tests keep running.
    /// </summary>
    public static void MapTptAsTph(this ModelBuilder modelBuilder, params Type[] roots)
    {
        foreach (var root in roots)
        {
            var builder = modelBuilder.Entity(root);
            builder.UseTphMappingStrategy();
            foreach (var derived in builder.Metadata.GetDerivedTypes())
            {
                derived.RemoveAnnotation(RelationalAnnotationNames.TableName);
            }
        }
    }

    /// <summary>
    /// The F1 model maps each City type into its circuit's table (table splitting), and the
    /// FanTpt and CircuitTpt hierarchies as TPT. The provider rejects both, so the cities get their
    /// own collections and the TPT hierarchies are mapped as TPH. This keeps the rest of the F1
    /// suites running. The F1 fixtures build their model in <c>BuildModelExternal</c>, not in
    /// <c>OnModelCreating</c>, so they call this there.
    /// </summary>
    public static void MapF1ForCouchbase(this ModelBuilder modelBuilder)
    {
        modelBuilder.MapToOwnCollections(
            typeof(Microsoft.EntityFrameworkCore.TestModels.ConcurrencyModel.City),
            typeof(Microsoft.EntityFrameworkCore.TestModels.ConcurrencyModel.CityTpt),
            typeof(Microsoft.EntityFrameworkCore.TestModels.ConcurrencyModel.CityTpc));
        modelBuilder.MapTptAsTph(
            typeof(Microsoft.EntityFrameworkCore.TestModels.ConcurrencyModel.FanTpt),
            typeof(Microsoft.EntityFrameworkCore.TestModels.ConcurrencyModel.CircuitTpt));
    }

    /// <summary>
    /// EF's relational model validator rejects primitive collections of primitive collections
    /// (<c>bool[][]</c>) on every relational provider, and the relational providers' own fixtures
    /// ignore those properties. Same here.
    /// </summary>
    public static void IgnoreNestedPrimitiveCollections(this ModelBuilder modelBuilder)
    {
        foreach (var type in modelBuilder.Model.GetEntityTypes().ToList())
        {
            foreach (var property in type.GetDeclaredProperties().ToList())
            {
                var element = property.GetElementType()?.ClrType;
                if (element != null && element != typeof(string) && element != typeof(byte[])
                    && typeof(System.Collections.IEnumerable).IsAssignableFrom(element))
                {
                    type.RemoveProperty(property);
                    type.AddIgnored(property.Name);
                }
            }
        }
    }
}

