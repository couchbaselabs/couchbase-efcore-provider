using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// Phase-0 empirical spike for extending <c>[CouchbaseMeta]</c> to cover
/// <c>META(alias).flags</c>/<c>.type</c>: before choosing CLR types for the new
/// <see cref="global::Couchbase.EntityFrameworkCore.Metadata.CouchbaseMetaField"/> values, observe
/// the actual JSON shape N1QL returns for both fields against a real document written by this
/// SDK's default (System.Text.Json) serializer -- bypassing EF Core and this provider entirely,
/// mirroring the established "observe real behavior first" discipline used for the original META
/// work and the DateTime-format spike.
/// </summary>
[Collection(CouchbaseTestingCollection.Name)]
public class MetaFlagsTypeSpikeTests(BloggingFixture fixture, ITestOutputHelper outputHelper)
{
    [Fact]
    public async Task MetaFlagsAndType_ForJsonDocument_ObserveActualShape()
    {
        var collectionName = "metaspike" + Guid.NewGuid().ToString("N");
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

            await collection.InsertAsync("1", new Dictionary<string, object> { ["Id"] = 1, ["Name"] = "spike" });

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

            var sql = $"SELECT RAW META(d) FROM `{bucketId}`.`{scopeId}`.`{collectionName}` AS d WHERE META(d).id = '1'";

            using var result = await cluster.QueryAsync<System.Text.Json.JsonElement>(
                sql,
                new global::Couchbase.Query.QueryOptions().ScanConsistency(global::Couchbase.Query.QueryScanConsistency.RequestPlus));

            var rowCount = 0;
            await foreach (var row in result.Rows)
            {
                rowCount++;
                var flags = row.GetProperty("flags");
                var type = row.GetProperty("type");
                outputHelper.WriteLine($"META(d) = {row}");
                outputHelper.WriteLine($"flags: ValueKind={flags.ValueKind}, value={flags}");
                outputHelper.WriteLine($"type: ValueKind={type.ValueKind}, value={type}");

                // The observed shape that CouchbaseMetaField.Flags/Type are designed around: flags
                // is a non-negative JSON number well within uint32 range (Couchbase's document
                // flags are a 32-bit value), and type is always a plain JSON string.
                Assert.Equal(System.Text.Json.JsonValueKind.Number, flags.ValueKind);
                Assert.True(flags.TryGetUInt32(out _), "flags should fit in a uint32.");
                Assert.Equal(System.Text.Json.JsonValueKind.String, type.ValueKind);
                Assert.Equal("json", type.GetString());
            }

            Assert.Equal(1, rowCount);
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
