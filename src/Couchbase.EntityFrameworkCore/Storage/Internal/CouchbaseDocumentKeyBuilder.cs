using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Update;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Builds <see cref="Infrastructure.CouchbaseDocumentKeyFormat.V2"/> document keys. The legacy
/// format lives in <see cref="Extensions.EntityTypeExtensions"/> and is deliberately left untouched.
/// </summary>
internal static class CouchbaseDocumentKeyBuilder
{
    internal const char EscapeCharacter = '\\';

    /// <summary>
    /// Joins the entry's primary key values, in the key's declared order, into one document key.
    /// Reads the values from the update entry rather than the CLR object, so shadow and
    /// field-mapped key properties work the same as ordinary ones.
    /// </summary>
    public static string BuildV2(IEntityType entityType, IUpdateEntry updateEntry, char delimiter)
    {
        var primaryKey = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException(
                $"Entity type '{entityType.DisplayName()}' has no primary key defined. " +
                "Keyless entity types cannot be saved to Couchbase.");

        var documentKey = new StringBuilder();
        var first = true;
        foreach (var property in primaryKey.Properties)
        {
            var rawValue = updateEntry.GetCurrentValue(property)
                ?? throw new InvalidOperationException(
                    $"The key property '{entityType.DisplayName()}.{property.Name}' is null, so a document key " +
                    "cannot be built for it.");

            // Format the value the way it is STORED (after any value converter), not the CLR value.
            // The key is rebuilt from the entity every time it is saved or deleted, and the entity is
            // materialised from the stored form, so a conversion that loses information (for
            // example HasUnixMillisDateTime() drops sub-millisecond ticks) would otherwise give the
            // reloaded entity a different key than the one it was inserted under: an update would
            // upsert a duplicate and a delete would miss.
            var value = CouchbaseDatabaseWrapper.ApplyConverter(property, rawValue)
                ?? throw new InvalidOperationException(
                    $"The value converter of the key property '{entityType.DisplayName()}.{property.Name}' " +
                    "returned null, so a document key cannot be built for it.");

            // Couchbase's query engine returns numbers as doubles, so a decimal comes back as the
            // shortest text of its double (about 17 significant digits). A decimal key with more
            // precision than that would be rebuilt from the reloaded entity as a different key: an
            // update would upsert a duplicate and a delete would miss. Rounding the key to match
            // would instead make distinct values collide and overwrite each other, so refuse it.
            if (value is decimal decimalKey && !DecimalSurvivesReload(decimalKey))
            {
                throw new InvalidOperationException(
                    $"The decimal key property '{entityType.DisplayName()}.{property.Name}' has the value {decimalKey}, " +
                    "which has more precision than Couchbase returns when the document is read back (about 17 " +
                    "significant digits), so its document key would change on reload. Use a key with fewer " +
                    "significant digits, or a different key type.");
            }

            if (!first)
            {
                documentKey.Append(delimiter);
            }

            first = false;

            // With the default byte[] mapping an empty array is stored as an empty string, and an
            // empty document id is invalid, so it is written as "~" (which Base64 never produces).
            // A custom converter's output is honored as is: reserving "~" there could collide with
            // a converted value that really is "~".
            var text = rawValue is byte[] { Length: 0 } && property.GetValueConverter() is null
                ? "~"
                : FormatPart(value);
            Escape(text, delimiter, documentKey);
        }

        if (documentKey.Length == 0)
        {
            throw new InvalidOperationException(
                $"The document key for entity type '{entityType.DisplayName()}' is empty, and Couchbase does not " +
                "allow an empty document id. Give the key property a non-empty value.");
        }

        return documentKey.ToString();
    }

    /// <summary>
    /// True if <paramref name="value"/> comes back unchanged after being stored and read back: the
    /// query engine hands back the shortest text of the value's double, and
    /// <c>CouchbaseDbDataReader.GetDecimal</c> parses that text as a decimal.
    /// </summary>
    internal static bool DecimalSurvivesReload(decimal value)
        => decimal.TryParse(
               ((double)value).ToString("R", CultureInfo.InvariantCulture),
               NumberStyles.Float,
               CultureInfo.InvariantCulture,
               out var reloaded)
           && reloaded == value;

    internal static string FormatPart(object value)
        => value switch
        {
            byte[] { Length: 0 } => "~",
            byte[] bytes => Convert.ToBase64String(bytes),
            string text => text,
            // A Local DateTime's "O" text carries the saving machine's offset, and a reload converts the
            // stored offset text to the READING machine's local time, so the same instant would format
            // differently across time zones. Normalise Local to UTC; Utc ("Z") and Unspecified (no
            // offset) are already machine independent.
            DateTime dateTime => (dateTime.Kind == DateTimeKind.Local ? dateTime.ToUniversalTime() : dateTime)
                .ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            // Explicit: the default TimeOnly pattern is the short "HH:mm", which would drop the seconds
            // and make distinct keys collide.
            TimeOnly timeOnly => timeOnly.ToString("O", CultureInfo.InvariantCulture),
            DateOnly dateOnly => dateOnly.ToString("O", CultureInfo.InvariantCulture),
            // Canonical form: "G29" drops trailing zeros, so 1234.50m and 1234.5m (what a reload gives
            // back, since the stored JSON number is read through a double) are the same key.
            decimal decimalValue => decimalValue.ToString("G29", CultureInfo.InvariantCulture),
            Enum enumValue => Convert.ToString(
                Convert.ChangeType(enumValue, Enum.GetUnderlyingType(enumValue.GetType()), CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture)!,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

    private static void Escape(string part, char delimiter, StringBuilder destination)
    {
        foreach (var character in part)
        {
            if (character == EscapeCharacter || character == delimiter)
            {
                destination.Append(EscapeCharacter);
            }

            destination.Append(character);
        }
    }
}
