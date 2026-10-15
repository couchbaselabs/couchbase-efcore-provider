using System.Text;
using Couchbase.Core.IO.Serializers;
using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Newtonsoft.Json;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// The provider reads every query row through the cluster's serializer. A user may configure their
/// own <see cref="ITypeSerializer"/> (for example one built on Newtonsoft.Json); the provider must
/// still be able to save and read entities with it.
/// </summary>
[Collection(CouchbaseTestingCollection.Name)]
public class CustomSerializerTests(BloggingFixture fixture)
{
    [Fact]
    public async Task NewtonsoftBasedCustomSerializer_CanSaveAndReadEntities()
    {
        var collectionName = "ser" + Guid.NewGuid().ToString("N");
        var serializer = new NewtonsoftTypeSerializer();

        var optionsBuilder = new DbContextOptionsBuilder<SerializerDbContext>();
        optionsBuilder.UseCouchbase(
            new global::Couchbase.ClusterOptions()
                .WithConnectionString(fixture.Host)
                .WithPasswordAuthentication(fixture.Username, fixture.Password)
                .WithSerializer(serializer),
            o =>
            {
                o.Bucket = fixture.BucketName;
                o.Scope = fixture.ScopeName;
                o.ScanConsistency = global::Couchbase.Query.QueryScanConsistency.RequestPlus;
                o.AutoCreateIndexes = true;
            });
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));

        try
        {
            await using (var writer = new SerializerDbContext(optionsBuilder.Options, collectionName))
            {
                await writer.Database.EnsureCreatedAsync();
                writer.Add(new SerializerEntity { Id = 1, Name = "custom", Count = 42, Created = new DateTime(2025, 1, 15, 10, 30, 0, DateTimeKind.Utc) });
                await writer.SaveChangesAsync();
            }

            // The custom serializer is kept and used to write the document.
            Assert.True(serializer.SerializeCalls > 0, "the configured serializer was never used to write a document");

            await using var reader = new SerializerDbContext(optionsBuilder.Options, collectionName);
            var loaded = await PollingHelper.PollForResultAsync(
                () => reader.Set<SerializerEntity>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == 1),
                result => result != null,
                TimeSpan.FromSeconds(10));

            Assert.NotNull(loaded);
            Assert.Equal("custom", loaded!.Name);
            Assert.Equal(42, loaded.Count);
            // Compare the instant: the exact value and Kind a DateTime comes back with is covered by
            // DateTimeKindRoundTripTests, not by this test about the serializer.
            Assert.Equal(new DateTime(2025, 1, 15, 10, 30, 0, DateTimeKind.Utc), loaded.Created.ToUniversalTime());
        }
        finally
        {
            await DropCollectionAsync(collectionName);
        }
    }

    private async Task DropCollectionAsync(string collectionName)
    {
        try
        {
            var clusterOptions = new global::Couchbase.ClusterOptions()
                .WithConnectionString(fixture.Host)
                .WithCredentials(fixture.Username, fixture.Password);
            using var cluster = await global::Couchbase.Cluster.ConnectAsync(clusterOptions);
            var bucket = await cluster.BucketAsync(fixture.BucketName);
            await bucket.Collections.DropCollectionAsync(fixture.ScopeName, collectionName);
        }
        catch (global::Couchbase.Management.Collections.CollectionNotFoundException)
        {
        }
    }

    public class SerializerEntity
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
        public DateTime Created { get; set; }
    }

    public class SerializerDbContext(DbContextOptions<SerializerDbContext> options, string collectionName)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<SerializerEntity>().ToCouchbaseCollection(this, collectionName);
    }

    /// <summary>A minimal Newtonsoft.Json based serializer; deliberately not a DefaultSerializer.</summary>
    private sealed class NewtonsoftTypeSerializer : ITypeSerializer
    {
        private readonly JsonSerializer _serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            DateParseHandling = DateParseHandling.None
        });

        public T? Deserialize<T>(ReadOnlyMemory<byte> buffer)
        {
            using var reader = new JsonTextReader(new StreamReader(new MemoryStream(buffer.ToArray()), Encoding.UTF8));
            return _serializer.Deserialize<T>(reader);
        }

        public T? Deserialize<T>(Stream stream)
        {
            using var reader = new JsonTextReader(new StreamReader(stream, Encoding.UTF8, leaveOpen: true, bufferSize: 1024, detectEncodingFromByteOrderMarks: false));
            return _serializer.Deserialize<T>(reader);
        }

        public ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default)
            => new(Deserialize<T>(stream));

        public int SerializeCalls { get; private set; }

        public void Serialize(Stream stream, object? obj)
        {
            SerializeCalls++;
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true);
            _serializer.Serialize(writer, obj);
        }

        public ValueTask SerializeAsync(Stream stream, object? obj, CancellationToken cancellationToken = default)
        {
            Serialize(stream, obj);
            return default;
        }

        public void Serialize<T>(Stream stream, T obj) => Serialize(stream, (object?)obj);

        public ValueTask SerializeAsync<T>(Stream stream, T obj, CancellationToken cancellationToken = default)
            => SerializeAsync(stream, (object?)obj, cancellationToken);
    }
}
