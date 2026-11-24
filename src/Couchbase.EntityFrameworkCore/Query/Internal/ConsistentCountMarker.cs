using Couchbase.Query;
using Microsoft.EntityFrameworkCore.Query;

namespace Couchbase.EntityFrameworkCore.Query.Internal;

/// <summary>
/// The value of a <see cref="CouchbaseQueryHintAnnotationNames.ConsistentCount"/> annotation until
/// the query runs. It reads the query's final <see cref="CouchbaseQueryExecutionOptions"/>, which a
/// later <c>WithScanConsistency</c> or <c>ConsistentWith</c> can still change.
/// </summary>
internal sealed class ConsistentCountMarker(
    CouchbaseQueryExecutionOptions executionOptions,
    QueryScanConsistency contextScanConsistency)
{
    /// <summary>
    /// True when the query runs with a scan consistency other than <c>NotBounded</c>, in the order
    /// <see cref="CouchbaseQueryEnumerable{T}"/> applies. Only the nullness of the
    /// <c>ConsistentWith</c> parameter is read, which is part of EF Core's SQL cache key.
    /// </summary>
    public bool RequiresIndexCount(ParametersCacheDecorator parameters)
        => CouchbaseQueryExecutionOptions.EffectiveScanConsistency(executionOptions.ScanConsistency, contextScanConsistency)
               != QueryScanConsistency.NotBounded
           || (executionOptions.ConsistentWithParameterName is { } parameterName && !parameters.IsNull(parameterName));
}
