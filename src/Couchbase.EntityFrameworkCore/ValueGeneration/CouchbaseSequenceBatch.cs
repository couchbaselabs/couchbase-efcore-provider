using Couchbase.Core.Exceptions;
using Couchbase.Core.Exceptions.Query;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.ValueGeneration;

/// <summary>
/// Fetches several values from a Couchbase sequence in one query.
/// </summary>
internal static class CouchbaseSequenceBatch
{
    // The query service's reason code for "datastore.sequence.not_found".
    private const int SequenceNotFoundCode = 19106;

    /// <summary>
    /// Returns <paramref name="count"/> new values from the sequence at <paramref name="location"/>,
    /// in ascending order.
    /// </summary>
    /// <exception cref="InvalidOperationException">The sequence does not exist.</exception>
    public static async Task<long[]> NextValuesAsync(
        DbContext context,
        CouchbaseSequenceLocation location,
        CouchbaseSequenceOptions options,
        int count,
        CancellationToken cancellationToken)
    {
        var sqlGenerationHelper = context.GetService<ISqlGenerationHelper>();
        var sequence = location.ToSql(sqlGenerationHelper);

        // One row per value: the query service increments a sequence once per document, and each
        // ARRAY_RANGE element is a separate document of the FROM clause. (One row holding an
        // ARRAY comprehension of NEXT VALUE FOR would rely on per-element behaviour that the
        // documentation does not describe.)
        var query = $"SELECT RAW NEXT VALUE FOR {sequence} FROM ARRAY_RANGE(0, {count}) AS x";

        var connection = context.GetService<IRelationalConnection>();
        var opened = await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var command = connection.DbConnection.CreateCommand();
            command.CommandText = query;

            var values = new List<long>(count);
            try
            {
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    values.Add(reader.GetInt64(0));
                }
            }
            catch (CouchbaseException e) when (IsSequenceNotFound(e))
            {
                throw new InvalidOperationException(
                    $"The Couchbase sequence {sequence} does not exist. Create it with EnsureCreatedAsync(), "
                    + $"or run: CREATE SEQUENCE {sequence} {options.ToSqlOptionsClause()}",
                    e);
            }

            if (values.Count != count)
            {
                throw new InvalidOperationException(
                    $"Expected {count} values from the Couchbase sequence {sequence}, but the query returned {values.Count}.");
            }

            values.Sort();
            return values.ToArray();
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private static bool IsSequenceNotFound(CouchbaseException exception)
        => exception.Context is QueryErrorContext { Errors: { } errors }
           && errors.Any(e => e.Code == SequenceNotFoundCode || e.Reason?.Code == SequenceNotFoundCode);
}
