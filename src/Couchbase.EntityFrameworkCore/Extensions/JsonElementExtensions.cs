using System.Text.Json;

namespace Couchbase.EntityFrameworkCore.Extensions;

internal static class JsonElementExtensions
{
    /// <summary>
    /// Case-insensitive property lookup on a <see cref="JsonElement"/>.
    /// Returns false (and <paramref name="value"/> = default) when the element is not an object.
    /// Tries an exact match first, then falls back to an OrdinalIgnoreCase linear scan.
    /// </summary>
    // CI = Case-Insensitive
    internal static bool TryGetPropertyCI(this JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }
        if (element.TryGetProperty(name, out value)) return true;
        foreach (var prop in element.EnumerateObject())
        {
            if (!string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = prop.Value;
            return true;
        }
        value = default;
        return false;
    }
}
