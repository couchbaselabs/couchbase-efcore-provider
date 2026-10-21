using Couchbase.Core.IO.Serializers;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// The serializer the provider uses to read its own query result rows.
/// </summary>
/// <remarks>
/// Result rows are read as <see cref="System.Text.Json.JsonElement"/>, which only System.Text.Json
/// can build. The cluster's serializer is the application's choice (the provider keeps a custom one
/// it is given), and a serializer such as one built on Newtonsoft.Json deserializes a
/// <c>JsonElement</c> into an empty value, so every column read back as null. Passing this
/// serializer on the provider's own queries makes reading independent of the cluster serializer.
/// It is not used for results the application asks to be materialised into its own types
/// (<c>FromSql</c>), which still use the configured serializer.
/// </remarks>
internal static class CouchbaseResultRowSerializer
{
    public static readonly ITypeSerializer Instance = SystemTextJsonSerializer.Create();
}
