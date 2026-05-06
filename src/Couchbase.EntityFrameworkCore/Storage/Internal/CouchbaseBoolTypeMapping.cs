using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Type mapping for boolean values in Couchbase SQL++.
/// </summary>
/// <remarks>
/// Couchbase SQL++ uses TRUE/FALSE literals for booleans, not 1/0.
/// </remarks>
public class CouchbaseBoolTypeMapping : BoolTypeMapping
{
    /// <summary>
    /// Creates a new instance of <see cref="CouchbaseBoolTypeMapping"/>.
    /// </summary>
    public CouchbaseBoolTypeMapping()
        : base("BOOLEAN")
    {
    }

    /// <summary>
    /// Creates a new instance from existing parameters (used for cloning).
    /// </summary>
    protected CouchbaseBoolTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
    {
        return new CouchbaseBoolTypeMapping(parameters);
    }

    /// <summary>
    /// Generates a SQL++ boolean literal (TRUE or FALSE).
    /// </summary>
    protected override string GenerateNonNullSqlLiteral(object value)
    {
        return (bool)value ? "TRUE" : "FALSE";
    }
}
