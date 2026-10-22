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

            if (!first)
            {
                documentKey.Append(delimiter);
            }

            first = false;

            // An empty byte[] would convert to an empty string, and an empty document id is invalid,
            // so it is written as "~" (which Base64 never produces).
            var text = rawValue is byte[] { Length: 0 } ? "~" : FormatPart(value);
            Escape(text, delimiter, documentKey);
        }

        return documentKey.ToString();
    }

    internal static string FormatPart(object value)
        => value switch
        {
            byte[] { Length: 0 } => "~",
            byte[] bytes => Convert.ToBase64String(bytes),
            string text => text,
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            // Explicit: the default TimeOnly pattern is the short "HH:mm", which would drop the seconds
            // and make distinct keys collide.
            TimeOnly timeOnly => timeOnly.ToString("O", CultureInfo.InvariantCulture),
            DateOnly dateOnly => dateOnly.ToString("O", CultureInfo.InvariantCulture),
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
