using System.Collections;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Type mapping for byte arrays in Couchbase, stored as Base64-encoded strings.
/// </summary>
/// <remarks>
/// Couchbase JSON documents store binary data as Base64 strings.
/// This mapping ensures SQL++ literals and parameters are correctly formatted as Base64.
/// </remarks>
public class CouchbaseByteArrayTypeMapping : RelationalTypeMapping
{
    private static readonly ValueConverter<byte[], string> Base64Converter =
        new(
            bytes => Convert.ToBase64String(bytes),
            base64 => Convert.FromBase64String(base64));

    /// <summary>
    /// Creates a new instance of <see cref="CouchbaseByteArrayTypeMapping"/>.
    /// </summary>
    public CouchbaseByteArrayTypeMapping()
        : base(new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(
                typeof(byte[]),
                Base64Converter,
                new ValueComparer<byte[]>(
                    (a, b) => StructuralComparisons.StructuralEqualityComparer.Equals(a, b),
                    v => StructuralComparisons.StructuralEqualityComparer.GetHashCode(v),
                    v => v.ToArray())),
            "STRING"))
    {
    }

    /// <summary>
    /// Creates a new instance from existing parameters (used for cloning).
    /// </summary>
    protected CouchbaseByteArrayTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
    {
        return new CouchbaseByteArrayTypeMapping(parameters);
    }

    /// <summary>
    /// Generates a SQL++ string literal containing the Base64-encoded byte array.
    /// </summary>
    protected override string GenerateNonNullSqlLiteral(object value)
    {
        // The value may already be converted to a Base64 string by the ValueConverter,
        // or it may be the original byte[]
        string base64;
        if (value is byte[] bytes)
        {
            base64 = Convert.ToBase64String(bytes);
        }
        else if (value is string str)
        {
            base64 = str;
        }
        else
        {
            throw new InvalidOperationException($"Cannot generate SQL literal for type {value.GetType()}");
        }
        
        // SQL++ string literals use single quotes
        return $"'{base64}'";
    }
}
