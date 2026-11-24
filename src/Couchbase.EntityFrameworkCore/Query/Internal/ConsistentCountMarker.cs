using Couchbase.Query;
using Microsoft.EntityFrameworkCore.Query;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

/// <summary>
/// The value of a <see cref="CouchbaseQueryHintAnnotationNames.ConsistentCount"/> annotation while a
/// query is compiled. It decides, when the query runs, whether a <c>COUNT</c> must go through an
/// index because the query needs read-your-writes consistency.
/// </summary>
/// <remarks>
/// The decision uses the query's final <see cref="CouchbaseQueryExecutionOptions"/>, which a
/// <c>WithScanConsistency</c> or <c>ConsistentWith</c> call can still change after the count is
/// translated, and whether the <c>ConsistentWith</c> <see cref="MutationState"/> is null, which is
/// only known from the parameter values. It follows the precedence that
/// <see cref="CouchbaseQueryEnumerable{T}"/> uses to build the query options.
/// </remarks>
internal sealed class ConsistentCountMarker(
    CouchbaseQueryExecutionOptions executionOptions,
    QueryScanConsistency contextScanConsistency)
{
    /// <summary>
    /// True when the query runs with a scan consistency other than
    /// <see cref="QueryScanConsistency.NotBounded"/>: the query's own <c>WithScanConsistency</c> or
    /// the context default, or <c>AtPlus</c> from a non-null <c>ConsistentWith</c> value. Only the
    /// nullness of the parameter is read, which is part of EF Core's SQL cache key, so the SQL can
    /// still be cached.
    /// </summary>
    public bool IsNeeded(ParametersCacheDecorator parameters)
    {
        if ((executionOptions.ScanConsistency ?? contextScanConsistency) != QueryScanConsistency.NotBounded)
        {
            return true;
        }

        return executionOptions.ConsistentWithParameterName is { } parameterName
               && !parameters.IsNull(parameterName);
    }
}
