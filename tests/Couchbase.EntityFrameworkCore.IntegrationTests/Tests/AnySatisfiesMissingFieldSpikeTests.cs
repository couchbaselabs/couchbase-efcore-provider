using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// Phase-0 empirical spike for the planned `.Any(predicate)`-over-`OwnsMany` fix: before writing
/// the <c>ANY x IN parentAlias.field SATISFIES ... END</c> rendering, this proves -- against a
/// real query engine, bypassing EF Core and this provider entirely (raw KV writes, raw N1QL via
/// <see cref="global::Couchbase.Cluster.QueryAsync{T}"/>) -- how N1QL's <c>ANY...SATISFIES</c>
/// behaves when the target array field is genuinely absent, present-but-empty, or present-but-
/// explicit-JSON-null on a given document. `.Any()`'s expected semantics for all three cases is
/// "no elements to match" == false, not an error.
/// </summary>
[Collection(CouchbaseTestingCollection.Name)]
public class AnySatisfiesMissingFieldSpikeTests(BloggingFixture fixture, ITestOutputHelper outputHelper)
{
    [Fact]
    public async Task AnySatisfies_OverAbsentEmptyNullAndPopulatedArray_EvaluatesFalsyNotError()
    {
        var collectionName = "anyspike" + Guid.NewGuid().ToString("N");
        var bucketId = fixture.BucketName;
        var scopeId = fixture.ScopeName;

        var clusterOptions = new global::Couchbase.ClusterOptions()
            .WithConnectionString(fixture.Host)
            .WithPasswordAuthentication(fixture.Username, fixture.Password)
            .WithSerializer(global::Couchbase.Core.IO.Serializers.SystemTextJsonSerializer.Create());
        using var cluster = await global::Couchbase.Cluster.ConnectAsync(clusterOptions);
        var bucket = await cluster.BucketAsync(fixture.BucketName);
        var scope = await bucket.ScopeAsync(fixture.ScopeName);

        try
        {
            await bucket.Collections.CreateCollectionAsync(
                new global::Couchbase.Management.Collections.CollectionSpec(fixture.ScopeName, collectionName));
            var collection = scope.Collection(collectionName);

            // Id=1: Tags field is genuinely MISSING (key omitted entirely).
            await collection.InsertAsync("1", new Dictionary<string, object> { ["Id"] = 1 });
            // Id=2: Tags field is present but an empty array.
            await collection.InsertAsync("2", new Dictionary<string, object> { ["Id"] = 2, ["Tags"] = new List<string>() });
            // Id=3: Tags field is present but an explicit JSON null.
            await collection.InsertAsync("3", new Dictionary<string, object?> { ["Id"] = 3, ["Tags"] = null });
            // Id=4: Tags field is present with a non-matching element.
            await collection.InsertAsync("4", new Dictionary<string, object> { ["Id"] = 4, ["Tags"] = new List<string> { "other" } });
            // Id=5: Tags field is present with a matching element.
            await collection.InsertAsync("5", new Dictionary<string, object> { ["Id"] = 5, ["Tags"] = new List<string> { "x" } });

            // A collection just created via the management API isn't necessarily visible to the
            // query service immediately -- retry the DDL itself (not just the online-wait below)
            // until the keyspace is recognized, rather than assuming instant propagation.
            var createIndexSql = $"CREATE PRIMARY INDEX IF NOT EXISTS ON `{bucketId}`.`{scopeId}`.`{collectionName}`";
            var createIndexDeadline = DateTime.UtcNow.AddSeconds(30);
            while (true)
            {
                try
                {
                    using var createIndexResult = await cluster.QueryAsync<dynamic>(createIndexSql);
                    await foreach (var _ in createIndexResult.Rows) { }
                    break;
                }
                catch (global::Couchbase.Core.Exceptions.IndexFailureException) when (DateTime.UtcNow < createIndexDeadline)
                {
                    await Task.Delay(500);
                }
            }

            // Poll until the index is online -- mirrors this project's established
            // AutoCreateIndexes wait pattern rather than a fixed sleep. A delay between polls
            // avoids hammering the query service, and failing fast if the deadline passes without
            // ever observing the index online turns a silent false-negative (querying against a
            // not-yet-ready index) into a clear, diagnosable failure instead.
            var deadline = DateTime.UtcNow.AddSeconds(60);
            var indexOnline = false;
            while (DateTime.UtcNow < deadline)
            {
                using var countResult = await cluster.QueryAsync<long>(
                    "SELECT RAW COUNT(*) FROM system:indexes WHERE is_primary = true AND state = 'online' " +
                    $"AND bucket_id = '{bucketId}' AND scope_id = '{scopeId}' AND keyspace_id = '{collectionName}'");
                long count = 0;
                await foreach (var row in countResult.Rows) { count = row; }
                if (count > 0)
                {
                    indexOnline = true;
                    break;
                }

                await Task.Delay(500);
            }

            Assert.True(indexOnline, $"Primary index on `{collectionName}` did not come online within the deadline.");

            var sql = $"SELECT RAW Id FROM `{bucketId}`.`{scopeId}`.`{collectionName}` AS d " +
                      "WHERE ANY v IN d.Tags SATISFIES v = 'x' END " +
                      "ORDER BY Id";

            using var result = await cluster.QueryAsync<long>(
                sql,
                new global::Couchbase.Query.QueryOptions().ScanConsistency(global::Couchbase.Query.QueryScanConsistency.RequestPlus));

            var matchedIds = new List<long>();
            await foreach (var row in result.Rows) { matchedIds.Add(row); }

            outputHelper.WriteLine($"ANY...SATISFIES matched ids: [{string.Join(", ", matchedIds)}]");

            // Absent (1), empty (2), explicit null (3), and non-matching (4) must all be excluded
            // -- falsy, not an error -- and only the genuinely matching document (5) included.
            Assert.Equal([5L], matchedIds);
        }
        finally
        {
            try
            {
                await bucket.Collections.DropCollectionAsync(fixture.ScopeName, collectionName);
            }
            catch (global::Couchbase.Management.Collections.CollectionNotFoundException)
            {
            }
        }
    }
}
