// Copyright 2025 Couchbase, Inc.
// Licensed under the Apache License, Version 2.0

using System.Diagnostics.CodeAnalysis;

namespace Couchbase.EntityFrameworkCore.Metadata;

/// <summary>
/// Represents a Couchbase keyspace consisting of Bucket, Scope, and Collection.
/// </summary>
/// <remarks>
/// This is the standard Couchbase keyspace format: <c>Bucket.Scope.Collection</c>.
/// The keyspace uniquely identifies where documents are stored in Couchbase Server.
/// </remarks>
public readonly record struct CouchbaseKeyspace
{
    /// <summary>
    /// Gets the bucket name.
    /// </summary>
    public string Bucket { get; }

    /// <summary>
    /// Gets the scope name.
    /// </summary>
    public string Scope { get; }

    /// <summary>
    /// Gets the collection name.
    /// </summary>
    public string Collection { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="CouchbaseKeyspace"/>.
    /// </summary>
    /// <param name="bucket">The bucket name.</param>
    /// <param name="scope">The scope name.</param>
    /// <param name="collection">The collection name.</param>
    public CouchbaseKeyspace(string bucket, string scope, string collection)
    {
        ArgumentException.ThrowIfNullOrEmpty(bucket);
        ArgumentException.ThrowIfNullOrEmpty(scope);
        ArgumentException.ThrowIfNullOrEmpty(collection);

        Bucket = bucket;
        Scope = scope;
        Collection = collection;
    }

    /// <summary>
    /// Returns the keyspace in standard format: <c>Bucket.Scope.Collection</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the instance was default-initialized (e.g. <c>default(CouchbaseKeyspace)</c>)
    /// and has null segments. Use the constructor or <see cref="Parse"/> instead.
    /// </exception>
    public override string ToString()
    {
        ThrowIfDefaultInitialized();
        return $"{Bucket}.{Scope}.{Collection}";
    }

    /// <summary>
    /// Returns the keyspace in SQL++ format with each segment backtick-delimited and any
    /// embedded backtick characters doubled: <c>`Bucket`.`Scope`.`Collection`</c>.
    /// </summary>
    /// <remarks>
    /// Prefer <c>ISqlGenerationHelper.DelimitIdentifier(keyspace.ToString())</c> inside the
    /// query-SQL generation pipeline — that path uses the provider's authoritative escaping
    /// logic.  This method is provided for contexts that do not have access to the helper
    /// (e.g. display, logging, serialisation).
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the instance was default-initialized (e.g. <c>default(CouchbaseKeyspace)</c>)
    /// and has null segments. Use the constructor or <see cref="Parse"/> instead.
    /// </exception>
    public string ToSqlString()
    {
        ThrowIfDefaultInitialized();
        return $"`{Bucket.Replace("`", "``")}`.`{Scope.Replace("`", "``")}`.`{Collection.Replace("`", "``")}`";
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when the struct was default-initialized,
    /// leaving <see cref="Bucket"/>, <see cref="Scope"/>, or <see cref="Collection"/> null.
    /// </summary>
    private void ThrowIfDefaultInitialized()
    {
        if (Bucket is null || Scope is null || Collection is null)
            throw new InvalidOperationException(
                "This CouchbaseKeyspace instance was default-initialized and has null segments. " +
                "Use the constructor or Parse() to create a valid instance.");
    }

    /// <summary>
    /// Parses a keyspace string in the format <c>Bucket.Scope.Collection</c>.
    /// </summary>
    /// <param name="keyspace">The keyspace string to parse.</param>
    /// <returns>A <see cref="CouchbaseKeyspace"/> instance.</returns>
    /// <exception cref="ArgumentException">Thrown when the keyspace format is invalid.</exception>
    public static CouchbaseKeyspace Parse(string keyspace)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyspace);

        var parts = keyspace.Split('.');
        if (parts.Length != 3)
        {
            throw new ArgumentException(
                $"Invalid keyspace format: '{keyspace}'. Expected format: Bucket.Scope.Collection",
                nameof(keyspace));
        }

        return new CouchbaseKeyspace(
            parts[0].Trim('`'),
            parts[1].Trim('`'),
            parts[2].Trim('`'));
    }

    /// <summary>
    /// Tries to parse a keyspace string in the format <c>Bucket.Scope.Collection</c>.
    /// </summary>
    /// <param name="keyspace">The keyspace string to parse.</param>
    /// <param name="result">When successful, contains the parsed keyspace.</param>
    /// <returns><c>true</c> if parsing succeeded; otherwise, <c>false</c>.</returns>
    public static bool TryParse(string? keyspace, [NotNullWhen(true)] out CouchbaseKeyspace? result)
    {
        result = null;
        if (string.IsNullOrEmpty(keyspace))
        {
            return false;
        }

        var parts = keyspace.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        var bucket = parts[0].Trim('`');
        var scope = parts[1].Trim('`');
        var collection = parts[2].Trim('`');

        if (string.IsNullOrEmpty(bucket) || string.IsNullOrEmpty(scope) || string.IsNullOrEmpty(collection))
        {
            return false;
        }

        result = new CouchbaseKeyspace(bucket, scope, collection);
        return true;
    }

    /// <summary>
    /// Resolves an entity's table name into its actual keyspace: parsed as a full
    /// <c>Bucket.Scope.Collection</c> reference (via <c>ToCouchbaseCollection</c>/
    /// <c>[CouchbaseKeyspace]</c>) when <paramref name="tableName"/> is in that form, falling back
    /// to a bare collection named <paramref name="tableName"/> in
    /// <paramref name="configuredBucket"/>/<paramref name="configuredScope"/> otherwise.
    /// </summary>
    /// <remarks>
    /// Shared so bucket resolution stays consistent between schema-management code (collection/
    /// index creation, in <c>CouchbaseDatabaseCreator</c>) and runtime value generation (Couchbase
    /// sequences, in <c>CouchbaseValueGeneratorSelector</c>) — both need to agree on which bucket a
    /// given entity's data (and anything derived from it, like a sequence) actually lives in.
    /// <paramref name="tableName"/> must be non-null and non-empty: a <see cref="CouchbaseKeyspace"/>
    /// always represents a genuine bucket/scope/collection triple, so there is no valid keyspace to
    /// return for an entity with no table of its own (e.g. an owned type). Callers that only need
    /// the bucket, and may have no table name at all, should use <see cref="ResolveBucket"/> instead.
    /// </remarks>
    public static CouchbaseKeyspace Resolve(string tableName, string configuredBucket, string configuredScope)
    {
        ArgumentException.ThrowIfNullOrEmpty(tableName);

        if (TryParse(tableName, out var keyspace))
        {
            return keyspace!.Value;
        }

        return new CouchbaseKeyspace(configuredBucket, configuredScope, tableName);
    }

    /// <summary>
    /// Resolves an entity's table name into just its actual bucket, falling back to
    /// <paramref name="configuredBucket"/> when <paramref name="tableName"/> isn't a full
    /// <c>Bucket.Scope.Collection</c> reference — including when it's <see langword="null"/> or
    /// empty (an entity with no table of its own, e.g. an owned type, or a non-entity
    /// <c>DeclaringType</c> in value generation). Unlike <see cref="Resolve"/>, this never
    /// constructs a <see cref="CouchbaseKeyspace"/>, so it has no non-empty-collection
    /// requirement to satisfy.
    /// </summary>
    public static string ResolveBucket(string? tableName, string configuredBucket)
        => TryParse(tableName, out var keyspace) ? keyspace!.Value.Bucket : configuredBucket;
}
