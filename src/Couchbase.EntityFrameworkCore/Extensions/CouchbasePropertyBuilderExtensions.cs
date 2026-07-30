using Couchbase.EntityFrameworkCore.Metadata;
using Couchbase.EntityFrameworkCore.Storage.Internal;
using Couchbase.EntityFrameworkCore.ValueGeneration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Couchbase.EntityFrameworkCore.Extensions;

/// <summary>
/// Extension methods for <see cref="PropertyBuilder"/> for Couchbase-specific configuration.
/// </summary>
public static class CouchbasePropertyBuilderExtensions
{
    /// <summary>
    /// Configures the property to have its value generated using a Couchbase sequence
    /// with default options.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sequence will be looked up in the bucket and scope configured on the DbContext.
    /// </para>
    /// <para>
    /// This overload clears any previously configured scope override, custom options, or
    /// auto-create settings, reverting to default behavior.
    /// </para>
    /// </remarks>
    /// <param name="propertyBuilder">The property builder.</param>
    /// <param name="sequenceName">The name of the sequence.</param>
    /// <returns>The same property builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// modelBuilder.Entity&lt;Order&gt;()
    ///     .Property(e => e.Id)
    ///     .UseSequence("order_seq");
    /// </code>
    /// </example>
    public static PropertyBuilder<TProperty> UseSequence<TProperty>(
        this PropertyBuilder<TProperty> propertyBuilder,
        string sequenceName)
    {
        ArgumentException.ThrowIfNullOrEmpty(sequenceName);

        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceNameAnnotation,
            sequenceName);

        // Clear any previous overrides to revert to defaults
        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceScopeAnnotation,
            null);
        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceOptionsAnnotation,
            null);
        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceAutoCreateAnnotation,
            null);

        propertyBuilder.ValueGeneratedOnAdd();

        return propertyBuilder;
    }

    /// <summary>
    /// Configures the property to have its value generated using a Couchbase sequence
    /// in the specified scope with default options.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scope specified here overrides the DbContext-level scope for this sequence.
    /// </para>
    /// <para>
    /// This overload clears any previously configured custom options or auto-create settings,
    /// reverting to default behavior.
    /// </para>
    /// </remarks>
    /// <param name="propertyBuilder">The property builder.</param>
    /// <param name="scope">The scope containing the sequence.</param>
    /// <param name="sequenceName">The name of the sequence.</param>
    /// <returns>The same property builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// modelBuilder.Entity&lt;Order&gt;()
    ///     .Property(e => e.Id)
    ///     .UseSequence("analytics", "order_seq");
    /// </code>
    /// </example>
    public static PropertyBuilder<TProperty> UseSequence<TProperty>(
        this PropertyBuilder<TProperty> propertyBuilder,
        string scope,
        string sequenceName)
    {
        ArgumentException.ThrowIfNullOrEmpty(scope);
        ArgumentException.ThrowIfNullOrEmpty(sequenceName);

        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceNameAnnotation,
            sequenceName);

        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceScopeAnnotation,
            scope);

        // Clear any previous options/auto-create overrides to revert to defaults
        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceOptionsAnnotation,
            null);
        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceAutoCreateAnnotation,
            null);

        propertyBuilder.ValueGeneratedOnAdd();

        return propertyBuilder;
    }

    /// <summary>
    /// Configures the property to have its value generated using a Couchbase sequence
    /// with custom options.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When using <see cref="Microsoft.EntityFrameworkCore.DbContext.Database"/>.<see cref="Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade.EnsureCreatedAsync"/>,
    /// the sequence will be automatically created with the specified options.
    /// </para>
    /// <para>
    /// This overload clears any previously configured scope override or auto-create settings,
    /// reverting to default behavior (auto-create enabled).
    /// </para>
    /// </remarks>
    /// <param name="propertyBuilder">The property builder.</param>
    /// <param name="sequenceName">The name of the sequence.</param>
    /// <param name="options">The sequence options (start value, increment, etc.).</param>
    /// <returns>The same property builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// modelBuilder.Entity&lt;Order&gt;()
    ///     .Property(e => e.Id)
    ///     .UseSequence("order_seq", new CouchbaseSequenceOptions
    ///     {
    ///         StartWith = 1000,
    ///         IncrementBy = 10
    ///     });
    /// </code>
    /// </example>
    public static PropertyBuilder<TProperty> UseSequence<TProperty>(
        this PropertyBuilder<TProperty> propertyBuilder,
        string sequenceName,
        CouchbaseSequenceOptions options)
    {
        ArgumentException.ThrowIfNullOrEmpty(sequenceName);
        ArgumentNullException.ThrowIfNull(options);

        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceNameAnnotation,
            sequenceName);

        // Clear any previous scope override to revert to default
        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceScopeAnnotation,
            null);

        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceOptionsAnnotation,
            options);

        // Clear any previous auto-create override to revert to default (true)
        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceAutoCreateAnnotation,
            null);

        propertyBuilder.ValueGeneratedOnAdd();

        return propertyBuilder;
    }

    /// <summary>
    /// Configures the property to have its value generated using a Couchbase sequence
    /// in the specified scope with custom options.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scope specified here overrides the DbContext-level scope for this sequence.
    /// </para>
    /// <para>
    /// This overload clears any previously configured auto-create settings,
    /// reverting to default behavior (auto-create enabled).
    /// </para>
    /// </remarks>
    /// <param name="propertyBuilder">The property builder.</param>
    /// <param name="scope">The scope containing the sequence.</param>
    /// <param name="sequenceName">The name of the sequence.</param>
    /// <param name="options">The sequence options (start value, increment, etc.).</param>
    /// <returns>The same property builder for method chaining.</returns>
    public static PropertyBuilder<TProperty> UseSequence<TProperty>(
        this PropertyBuilder<TProperty> propertyBuilder,
        string scope,
        string sequenceName,
        CouchbaseSequenceOptions options)
    {
        ArgumentException.ThrowIfNullOrEmpty(scope);
        ArgumentException.ThrowIfNullOrEmpty(sequenceName);
        ArgumentNullException.ThrowIfNull(options);

        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceNameAnnotation,
            sequenceName);

        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceScopeAnnotation,
            scope);

        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceOptionsAnnotation,
            options);

        // Clear any previous auto-create override to revert to default (true)
        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceAutoCreateAnnotation,
            null);

        propertyBuilder.ValueGeneratedOnAdd();

        return propertyBuilder;
    }

    /// <summary>
    /// Configures the property to have its value generated using a Couchbase sequence
    /// with default options.
    /// </summary>
    /// <remarks>
    /// This overload clears any previously configured scope override, custom options, or
    /// auto-create settings, reverting to default behavior.
    /// </remarks>
    /// <param name="propertyBuilder">The property builder.</param>
    /// <param name="sequenceName">The name of the sequence.</param>
    /// <returns>The same property builder for method chaining.</returns>
    public static PropertyBuilder UseSequence(
        this PropertyBuilder propertyBuilder,
        string sequenceName)
    {
        ArgumentException.ThrowIfNullOrEmpty(sequenceName);

        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceNameAnnotation,
            sequenceName);

        // Clear any previous overrides to revert to defaults
        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceScopeAnnotation,
            null);
        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceOptionsAnnotation,
            null);
        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceAutoCreateAnnotation,
            null);

        propertyBuilder.ValueGeneratedOnAdd();

        return propertyBuilder;
    }

    /// <summary>
    /// Configures the property to have its value generated using a Couchbase sequence
    /// in the specified scope with default options.
    /// </summary>
    /// <remarks>
    /// This overload clears any previously configured custom options or auto-create settings,
    /// reverting to default behavior.
    /// </remarks>
    /// <param name="propertyBuilder">The property builder.</param>
    /// <param name="scope">The scope containing the sequence.</param>
    /// <param name="sequenceName">The name of the sequence.</param>
    /// <returns>The same property builder for method chaining.</returns>
    public static PropertyBuilder UseSequence(
        this PropertyBuilder propertyBuilder,
        string scope,
        string sequenceName)
    {
        ArgumentException.ThrowIfNullOrEmpty(scope);
        ArgumentException.ThrowIfNullOrEmpty(sequenceName);

        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceNameAnnotation,
            sequenceName);

        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceScopeAnnotation,
            scope);

        // Clear any previous options/auto-create overrides to revert to defaults
        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceOptionsAnnotation,
            null);
        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceAutoCreateAnnotation,
            null);

        propertyBuilder.ValueGeneratedOnAdd();

        return propertyBuilder;
    }

    #region GUID Value Generation

    /// <summary>
    /// Configures the property to have its value generated as a new GUID when an entity is added.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This uses EF Core's built-in client-side GUID generation. The GUID is generated
    /// before the entity is saved to the database, making it available immediately after
    /// calling <c>Add</c> or <c>AddAsync</c>.
    /// </para>
    /// <para>
    /// GUIDs are suitable for distributed systems where coordination between nodes for
    /// sequential ID generation is impractical.
    /// </para>
    /// </remarks>
    /// <param name="propertyBuilder">The property builder.</param>
    /// <returns>The same property builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// modelBuilder.Entity&lt;Order&gt;()
    ///     .Property(e => e.Id)
    ///     .UseGuid();
    /// </code>
    /// </example>
    public static PropertyBuilder<Guid> UseGuid(this PropertyBuilder<Guid> propertyBuilder)
    {
        // Clear any previous value generation annotations
        propertyBuilder.HasAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation, null);
        propertyBuilder.HasAnnotation(CouchbaseValueGeneratorSelector.SequenceScopeAnnotation, null);
        propertyBuilder.HasAnnotation(CouchbaseValueGeneratorSelector.SequenceOptionsAnnotation, null);
        propertyBuilder.HasAnnotation(CouchbaseValueGeneratorSelector.SequenceAutoCreateAnnotation, null);
        propertyBuilder.HasAnnotation(CouchbaseValueGeneratorSelector.GuidStringFormatAnnotation, null);

        // EF Core will automatically use GuidValueGenerator for Guid properties marked as ValueGeneratedOnAdd
        propertyBuilder.ValueGeneratedOnAdd();

        return propertyBuilder;
    }

    /// <summary>
    /// Configures the property to have its value generated as a new GUID when an entity is added.
    /// </summary>
    /// <remarks>
    /// This uses EF Core's built-in client-side GUID generation.
    /// The property must be of type <see cref="Guid"/>.
    /// </remarks>
    /// <param name="propertyBuilder">The property builder.</param>
    /// <returns>The same property builder for method chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the property type is not <see cref="Guid"/>.
    /// </exception>
    public static PropertyBuilder UseGuid(this PropertyBuilder propertyBuilder)
    {
        var clrType = propertyBuilder.Metadata.ClrType;
        if (clrType != typeof(Guid))
        {
            throw new InvalidOperationException(
                $"UseGuid() can only be used on properties of type Guid, but property " +
                $"'{propertyBuilder.Metadata.DeclaringType.ClrType.Name}.{propertyBuilder.Metadata.Name}' " +
                $"is of type '{clrType.Name}'. For string properties, use UseGuidString() instead.");
        }

        // Clear any previous value generation annotations
        propertyBuilder.HasAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation, null);
        propertyBuilder.HasAnnotation(CouchbaseValueGeneratorSelector.SequenceScopeAnnotation, null);
        propertyBuilder.HasAnnotation(CouchbaseValueGeneratorSelector.SequenceOptionsAnnotation, null);
        propertyBuilder.HasAnnotation(CouchbaseValueGeneratorSelector.SequenceAutoCreateAnnotation, null);
        propertyBuilder.HasAnnotation(CouchbaseValueGeneratorSelector.GuidStringFormatAnnotation, null);

        // EF Core will automatically use GuidValueGenerator for Guid properties marked as ValueGeneratedOnAdd
        propertyBuilder.ValueGeneratedOnAdd();

        return propertyBuilder;
    }

    private static readonly HashSet<string> ValidGuidFormats = new() { "D", "N", "B", "P" };

    /// <summary>
    /// Configures the property to have its value generated as a new GUID string when an entity is added.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This generates a GUID and converts it to a string format. Useful when the property type
    /// is <c>string</c> but you want GUID-based unique identifiers.
    /// </para>
    /// <para>
    /// The format parameter controls how the GUID is formatted:
    /// <list type="bullet">
    /// <item><c>"D"</c> (default): 32 digits with hyphens (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)</item>
    /// <item><c>"N"</c>: 32 digits without hyphens</item>
    /// <item><c>"B"</c>: 32 digits with hyphens, enclosed in braces</item>
    /// <item><c>"P"</c>: 32 digits with hyphens, enclosed in parentheses</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="propertyBuilder">The property builder.</param>
    /// <param name="format">The GUID string format (default: "D").</param>
    /// <returns>The same property builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// modelBuilder.Entity&lt;Order&gt;()
    ///     .Property(e => e.Id)
    ///     .UseGuidString("N"); // No hyphens
    /// </code>
    /// </example>
    public static PropertyBuilder<string> UseGuidString(
        this PropertyBuilder<string> propertyBuilder,
        string? format = "D")
    {
        // Normalize and validate the format
        var normalizedFormat = format ?? "D";

        if (string.IsNullOrEmpty(normalizedFormat))
        {
            throw new ArgumentException(
                "GUID format cannot be empty. Valid formats are: D, N, B, P.",
                nameof(format));
        }

        if (!ValidGuidFormats.Contains(normalizedFormat))
        {
            throw new ArgumentException(
                $"Invalid GUID format '{normalizedFormat}'. Valid formats are: D (hyphenated), N (no hyphens), B (braces), P (parentheses).",
                nameof(format));
        }

        // Clear any previous value generation annotations
        propertyBuilder.HasAnnotation(CouchbaseValueGeneratorSelector.SequenceNameAnnotation, null);
        propertyBuilder.HasAnnotation(CouchbaseValueGeneratorSelector.SequenceScopeAnnotation, null);
        propertyBuilder.HasAnnotation(CouchbaseValueGeneratorSelector.SequenceOptionsAnnotation, null);
        propertyBuilder.HasAnnotation(CouchbaseValueGeneratorSelector.SequenceAutoCreateAnnotation, null);

        // Store the validated format for the value generator
        propertyBuilder.HasAnnotation(CouchbaseValueGeneratorSelector.GuidStringFormatAnnotation, normalizedFormat);

        propertyBuilder.ValueGeneratedOnAdd();

        return propertyBuilder;
    }

    #endregion

    #region DateTime Format Override

    /// <summary>
    /// Overrides the .NET custom <see cref="DateTime"/> format string this provider uses for this
    /// property, instead of the DbContext-wide
    /// <see cref="Couchbase.EntityFrameworkCore.Infrastructure.CouchbaseDbContextOptionsBuilder.DateTimeFormat"/>
    /// default.
    /// </summary>
    /// <remarks>
    /// N1QL has no native date type — dates are just JSON strings — so nothing stops different
    /// properties (or data written by another system) from using a different string convention
    /// than the rest of the model. Only applies to the <c>.Date</c> member translator and inline
    /// <see cref="DateTime"/> literals for this specific property; the static <c>.Now</c>/
    /// <c>.UtcNow</c>/<c>.Today</c> translators have no associated property and always use the
    /// context-wide default. Equivalent to applying
    /// <see cref="Couchbase.EntityFrameworkCore.Metadata.DateTimeFormatAttribute"/> to the property.
    /// </remarks>
    /// <param name="propertyBuilder">The property builder.</param>
    /// <param name="format">The .NET custom <see cref="DateTime"/> format string.</param>
    /// <returns>The same property builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// modelBuilder.Entity&lt;Order&gt;()
    ///     .Property(e => e.ShipDate)
    ///     .HasDateTimeFormat("yyyy-MM-dd");
    /// </code>
    /// </example>
    public static PropertyBuilder<TProperty> HasDateTimeFormat<TProperty>(
        this PropertyBuilder<TProperty> propertyBuilder,
        string format)
    {
        ArgumentException.ThrowIfNullOrEmpty(format);

        var clrType = propertyBuilder.Metadata.ClrType;
        if (clrType != typeof(DateTime) && clrType != typeof(DateTime?))
        {
            throw new InvalidOperationException(
                $"HasDateTimeFormat() can only be used on properties of type DateTime or DateTime?, but property " +
                $"'{propertyBuilder.Metadata.DeclaringType.ClrType.Name}.{propertyBuilder.Metadata.Name}' " +
                $"is of type '{clrType.Name}'.");
        }

        propertyBuilder.HasAnnotation(CouchbaseTypeMappingSource.DateTimeFormatAnnotation, format);

        return propertyBuilder;
    }

    #endregion

    #region META() Field Override

    /// <summary>
    /// Sources this property's value from N1QL's <c>META()</c> function instead of a JSON document
    /// field. See <see cref="Couchbase.EntityFrameworkCore.Metadata.CouchbaseMetaField"/> for the
    /// supported fields. Equivalent to applying
    /// <see cref="Couchbase.EntityFrameworkCore.Metadata.CouchbaseMetaAttribute"/> to the property.
    /// </summary>
    /// <remarks>
    /// For <see cref="Couchbase.EntityFrameworkCore.Metadata.CouchbaseMetaField.Cas"/>, also call
    /// <c>.IsConcurrencyToken()</c> so EF Core treats it as an optimistic-concurrency token — the
    /// two are required together deliberately, so this provider's CAS-specific write-path behavior
    /// is never silently triggered just by adding <c>.IsConcurrencyToken()</c> to an unrelated
    /// property.
    /// </remarks>
    /// <param name="propertyBuilder">The property builder.</param>
    /// <param name="field">The document-metadata field this property is sourced from.</param>
    /// <returns>The same property builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// modelBuilder.Entity&lt;Order&gt;()
    ///     .Property(e => e.Cas)
    ///     .HasCouchbaseMeta(CouchbaseMetaField.Cas)
    ///     .IsConcurrencyToken();
    /// </code>
    /// </example>
    public static PropertyBuilder<TProperty> HasCouchbaseMeta<TProperty>(
        this PropertyBuilder<TProperty> propertyBuilder,
        CouchbaseMetaField field)
    {
        var expectedClrType = CouchbaseMetaFieldClrTypes.Get(field);
        var clrType = propertyBuilder.Metadata.ClrType;
        if (clrType != expectedClrType)
        {
            throw new InvalidOperationException(
                $"HasCouchbaseMeta({field}) can only be used on properties of type '{CouchbaseMetaFieldClrTypes.GetDisplayName(field)}', " +
                $"but property '{propertyBuilder.Metadata.DeclaringType.ClrType.Name}.{propertyBuilder.Metadata.Name}' " +
                $"is of type '{clrType.Name}'.");
        }

        propertyBuilder.HasAnnotation(CouchbaseMetaAnnotationNames.MetaField, field.ToString());
        propertyBuilder.ValueGeneratedOnAddOrUpdate();

        return propertyBuilder;
    }

    #endregion
}
