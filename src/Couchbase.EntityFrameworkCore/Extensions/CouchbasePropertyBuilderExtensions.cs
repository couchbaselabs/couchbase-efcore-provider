using Couchbase.EntityFrameworkCore.ValueGeneration;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Couchbase.EntityFrameworkCore.Extensions;

/// <summary>
/// Extension methods for <see cref="PropertyBuilder"/> for Couchbase-specific configuration.
/// </summary>
public static class CouchbasePropertyBuilderExtensions
{
    /// <summary>
    /// Configures the property to have its value generated using a Couchbase sequence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sequence must already exist in the database. Use the Couchbase SQL++ statement
    /// <c>CREATE SEQUENCE bucket.scope.sequence_name</c> to create the sequence.
    /// </para>
    /// <para>
    /// The sequence will be looked up in the bucket and scope configured on the DbContext.
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

        // Clear any previous scope override to revert to DbContext-level scope
        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceScopeAnnotation,
            null);

        propertyBuilder.ValueGeneratedOnAdd();

        return propertyBuilder;
    }

    /// <summary>
    /// Configures the property to have its value generated using a Couchbase sequence
    /// in the specified scope.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sequence must already exist in the database. Use the Couchbase SQL++ statement
    /// <c>CREATE SEQUENCE bucket.scope.sequence_name</c> to create the sequence.
    /// </para>
    /// <para>
    /// The scope specified here overrides the DbContext-level scope for this sequence.
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

        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceScopeAnnotation,
            null);

        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceOptionsAnnotation,
            options);

        propertyBuilder.ValueGeneratedOnAdd();

        return propertyBuilder;
    }

    /// <summary>
    /// Configures the property to have its value generated using a Couchbase sequence
    /// in the specified scope with custom options.
    /// </summary>
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

        propertyBuilder.ValueGeneratedOnAdd();

        return propertyBuilder;
    }

    /// <summary>
    /// Configures the property to have its value generated using a Couchbase sequence.
    /// </summary>
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

        // Clear any previous scope override to revert to DbContext-level scope
        propertyBuilder.HasAnnotation(
            CouchbaseValueGeneratorSelector.SequenceScopeAnnotation,
            null);

        propertyBuilder.ValueGeneratedOnAdd();

        return propertyBuilder;
    }

    /// <summary>
    /// Configures the property to have its value generated using a Couchbase sequence
    /// in the specified scope.
    /// </summary>
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

        propertyBuilder.ValueGeneratedOnAdd();

        return propertyBuilder;
    }
}
