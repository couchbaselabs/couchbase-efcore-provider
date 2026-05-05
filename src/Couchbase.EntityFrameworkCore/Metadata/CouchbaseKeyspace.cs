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
    public override string ToString() => $"{Bucket}.{Scope}.{Collection}";

    /// <summary>
    /// Returns the keyspace in SQL++ format with backtick escaping: <c>`Bucket`.`Scope`.`Collection`</c>.
    /// </summary>
    public string ToSqlString() => $"`{Bucket}`.`{Scope}`.`{Collection}`";

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
}
