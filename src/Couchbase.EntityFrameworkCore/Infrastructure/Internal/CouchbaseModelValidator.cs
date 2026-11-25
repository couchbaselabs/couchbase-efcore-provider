using Couchbase.EntityFrameworkCore.Metadata;
using Couchbase.EntityFrameworkCore.ValueGeneration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Couchbase.EntityFrameworkCore.Infrastructure.Internal;

public class CouchbaseModelValidator : RelationalModelValidator
{
    private readonly string? _bucket;
    private readonly string? _scope;

    public CouchbaseModelValidator(ModelValidatorDependencies dependencies,
        RelationalModelValidatorDependencies relationalDependencies) : this(dependencies, relationalDependencies, null, null)
    {
    }

    /// <param name="dependencies">The validator's dependencies.</param>
    /// <param name="relationalDependencies">The relational validator's dependencies.</param>
    /// <param name="bucket">The context's bucket, used to resolve bare collection names.</param>
    /// <param name="scope">The context's scope, used to resolve bare collection names.</param>
    public CouchbaseModelValidator(ModelValidatorDependencies dependencies,
        RelationalModelValidatorDependencies relationalDependencies,
        string? bucket, string? scope) : base(dependencies, relationalDependencies)
    {
        _bucket = bucket;
        _scope = scope;
    }

    public override void Validate(IModel model, IDiagnosticsLogger<DbLoggerCategory.Model.Validation> logger)
    {
        // Before the base validation, so that these mappings get a message that says what to do
        // instead. EF Core rejects some of them (unrelated types in one table) with its own message.
        ValidateNoTablePerType(model);
        ValidateNoTableSplitting(model);
        base.Validate(model, logger);
        ValidateSequenceTypes(model);
    }

    /// <summary>
    /// The provider does not support TPT. A TPT query joins the base type's collection to the
    /// derived type's collection, but the save path writes each entity as one document in its own
    /// type's collection. So the base collection stays empty and the queries find nothing.
    /// </summary>
    private void ValidateNoTablePerType(IModel model)
    {
        foreach (var root in model.GetEntityTypes().Where(e => e.BaseType == null && !e.IsOwned()))
        {
            var hierarchy = root.GetDerivedTypesInclusive().ToList();
            if (hierarchy.Count < 2)
            {
                continue;
            }

            var strategy = root.GetMappingStrategy();
            var rootTable = root.GetTableName();
            if (strategy != RelationalAnnotationNames.TptMappingStrategy
                && (strategy == RelationalAnnotationNames.TpcMappingStrategy
                    || hierarchy.All(e => e.GetTableName() == rootTable)))
            {
                continue;
            }

            var mappings = string.Join(", ", hierarchy.Select(e =>
                $"'{e.DisplayName()}' to '{(e.GetTableName() is { } table ? CollectionOf(table) : "(none)")}'"));
            throw new InvalidOperationException(
                $"The entity type hierarchy of '{root.DisplayName()}' uses table-per-type (TPT) mapping: {mappings}. "
                + "The Couchbase provider does not support TPT. Use table-per-hierarchy (TPH) instead: "
                + "map the derived types to the collection of the base type, and do not call UseTptMappingStrategy.");
        }
    }

    /// <summary>
    /// The provider writes one document per entity, and the document key comes only from the
    /// primary key values. When two entity types share a collection and a key (table splitting),
    /// both write to the same document key: an insert fails with a document-exists error and an
    /// update overwrites the other type's fields. So the provider rejects the model.
    /// </summary>
    /// <remarks>
    /// Types in one inheritance hierarchy (TPH) share a collection; that is supported. An owned
    /// type is stored inside its owner's document, so it is not checked. A keyless type is never
    /// saved, so it cannot overwrite a document, and it is not checked either. A bare collection
    /// name is resolved with the context's bucket and scope, so <c>orders</c> and
    /// <c>bucket.scope.orders</c> are the same collection.
    /// </remarks>
    private void ValidateNoTableSplitting(IModel model)
    {
        var collections = model.GetEntityTypes()
            .Where(e => !e.IsOwned() && e.FindPrimaryKey() != null && e.GetTableName() != null)
            .GroupBy(e => CollectionOf(e.GetTableName()!));

        foreach (var collection in collections)
        {
            var roots = collection.Select(e => e.GetRootType()).Distinct().ToList();
            if (roots.Count < 2)
            {
                continue;
            }

            var names = string.Join(", ", roots.Select(e => $"'{e.DisplayName()}'"));
            throw new InvalidOperationException(
                $"The entity types {names} are mapped to the same collection '{collection.Key}'. "
                + "The Couchbase provider does not support table splitting: each entity is stored as its own "
                + "document, keyed by its primary key, so these entity types would write to the same document. "
                + "Map each entity type to its own collection, or make the dependent type an owned type "
                + "(OwnsOne), which is stored inside the principal's document.");
        }
    }

    /// <summary>
    /// The full <c>bucket.scope.collection</c> name of a table. A bare name stays as it is when the
    /// context has no bucket or scope.
    /// </summary>
    private string CollectionOf(string table)
        => CouchbaseKeyspace.TryParse(table, out var keyspace) ? keyspace!.Value.ToString()
            : string.IsNullOrEmpty(_bucket) || string.IsNullOrEmpty(_scope) ? table
            : new CouchbaseKeyspace(_bucket, _scope, table).ToString();

    /// <summary>
    /// A TPC hierarchy stores each concrete type in its own collection, so a generated key must be
    /// unique across them. EF Core warns about any generated TPC key, because an identity column
    /// generates values per table. A key drawn from a Couchbase sequence is shared by the whole
    /// hierarchy, so it is accepted; any other generated TPC key still gets the warning.
    /// </summary>
    protected override void ValidateValueGeneration(
        IEntityType entityType, IKey key, IDiagnosticsLogger<DbLoggerCategory.Model.Validation> logger)
    {
        if (key.Properties.All(p => p.ValueGenerated == ValueGenerated.Never
                || p.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation) != null))
        {
            return;
        }

        base.ValidateValueGeneration(entityType, key, logger);
    }

    /// <summary>
    /// A Couchbase sequence produces integers, so it can only feed an integer (or decimal) property.
    /// </summary>
    private static void ValidateSequenceTypes(IModel model)
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            foreach (var property in entityType.GetDeclaredProperties())
            {
                if (property.FindAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation) is null)
                {
                    continue;
                }

                var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (!CouchbaseSequenceTypes.IsSupported(type))
                {
                    throw new InvalidOperationException(
                        $"Cannot use Couchbase sequence for property '{entityType.DisplayName()}.{property.Name}' "
                        + $"because the property type '{property.ClrType.Name}' is not supported. "
                        + "Supported types are: int, long, short, byte, uint, ulong, ushort, decimal.");
                }
            }
        }
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
