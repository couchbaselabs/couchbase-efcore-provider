using Couchbase; // ICluster, IBucket
using Couchbase.Extensions.DependencyInjection;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

/// <summary>
/// Wraps an application-registered <see cref="IClusterProvider"/> so that EF Core's internal
/// service provider can resolve it without taking ownership of its lifetime. Per Couchbase
/// guidance a single <see cref="ICluster"/> is shared per process; the application owns and
/// disposes it, so <see cref="DisposeAsync"/> here is intentionally a no-op.
/// </summary>
internal sealed class NonOwningClusterProvider(IClusterProvider inner) : IClusterProvider
{
    public ValueTask<ICluster> GetClusterAsync() => inner.GetClusterAsync();

    // Do not dispose the application-owned cluster provider.
    public void Dispose() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// An <see cref="IBucketProvider"/> that opens buckets from a shared <see cref="IClusterProvider"/>
/// (one cluster, many buckets). Used when a context binds to an application-registered cluster so
/// that all of the provider's data paths reuse the same <see cref="ICluster"/> instead of each
/// context spinning up its own.
/// </summary>
internal sealed class SharedClusterBucketProvider(IClusterProvider clusterProvider) : IBucketProvider
{
    public async ValueTask<IBucket> GetBucketAsync(string bucketName)
    {
        var cluster = await clusterProvider.GetClusterAsync().ConfigureAwait(false);
        return await cluster.BucketAsync(bucketName).ConfigureAwait(false);
    }

    // The cluster provider is application-owned; this adapter holds only a reference.
    public void Dispose() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
