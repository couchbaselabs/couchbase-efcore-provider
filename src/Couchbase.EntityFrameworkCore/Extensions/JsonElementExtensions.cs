using System.Text.Json;

namespace Couchbase.EntityFrameworkCore.Extensions;

internal static class JsonElementExtensions
{
    /// <summary>
    /// Case-insensitive property lookup on a <see cref="JsonElement"/>.
    /// Tries an exact match first (O(m) but hits immediately when casing matches),
    /// then falls back to an OrdinalIgnoreCase linear scan.
    /// </summary>
    // CI = Case-Insensitive
    internal static bool TryGetPropertyCI(this JsonElement element, string name, out JsonElement value)
    {
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
