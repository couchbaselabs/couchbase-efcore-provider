namespace Couchbase.EntityFrameworkCore.ValueGeneration;

/// <summary>
/// The CLR types a Couchbase sequence can generate values for.
/// </summary>
internal static class CouchbaseSequenceTypes
{
    private static readonly HashSet<Type> SupportedTypes =
    [
        typeof(int),
        typeof(long),
        typeof(short),
        typeof(byte),
        typeof(uint),
        typeof(ulong),
        typeof(ushort),
        typeof(decimal)
    ];

    /// <summary>
    /// Whether a sequence can generate values for <paramref name="type"/> (not a nullable type:
    /// pass the underlying type).
    /// </summary>
    public static bool IsSupported(Type type) => SupportedTypes.Contains(type);
}
