using System.Diagnostics;
using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Tests;

/// <summary>
/// Load/perf validation against a real (locally-containerized, single-node) Couchbase cluster via
/// the same Aspire-managed infrastructure the rest of the integration suite uses. These are not
/// pass/fail correctness tests — they exist to produce real, measured numbers backing the road-
/// to-GA perf claims (see the provider's perf notes) instead of assuming the write-path
/// parallelization and read-path JOIN-based materialization actually pay off. Each scenario logs
/// throughput/latency via ITestOutputHelper and asserts only a loose, meaningful regression guard
/// (e.g. "batching beats doing it one at a time"), not fixed absolute thresholds — those would be
/// flaky across hardware/CI runners.
///
/// Caveat: this is a single local container, not a multi-node "representative" cluster — real
/// network latency to a remote cluster would change the read/write ratio further in the parallel
/// /batched paths' favor (more RTT to amortize), so these numbers are a conservative lower bound
/// on the benefit, not an upper one.
/// </summary>
[Collection(CouchbaseTestingCollection.Name)]
public class PerformanceTests(
    BloggingFixture bloggingFixture,
    OwnedTypeFixture ownedTypeFixture,
    ITestOutputHelper outputHelper)
{
    // Reserved id ranges well above anything else in the suite, to avoid any collision with
    // fixed/random ids used by other tests sharing these fixtures' buckets.
    private const int BlogIdBase = 900_000_000;
    private const int CustomerIdBase = 900_000;

    // Each scenario below gets its own fixed sub-range, offset by an amount independent of any
    // scenario's local `count` — deriving an offset from `count` (e.g. `BlogIdBase + 2 * count`)
    // silently collides across scenarios whenever they use different counts, since the ranges no
    // longer tile. 10_000 spacing is comfortably above the largest count used (200).
    private const int WriteBulkBlogIdBase = BlogIdBase;
    private const int WriteIndividualBlogIdBase = BlogIdBase + 10_000;
    private const int ReadBulkBlogIdBase = BlogIdBase + 20_000;
    private const int OwnedComparisonPlainBlogIdBase = BlogIdBase + 30_000;

    private static (double P50, double P95, double P99, double Min, double Max, double Mean) ComputeStats(List<double> samplesMs)
    {
        var sorted = samplesMs.OrderBy(x => x).ToList();
        double Percentile(double p) => sorted[(int)Math.Clamp(Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1)];
        return (Percentile(0.50), Percentile(0.95), Percentile(0.99), sorted[0], sorted[^1], sorted.Average());
    }

    private void Report(string label, int count, double totalMs, List<double>? perCallMs = null)
    {
        var throughput = count / (totalMs / 1000.0);
        var line = $"{label}: {count} docs in {totalMs:F0} ms ({throughput:F1} docs/sec)";
        if (perCallMs is { Count: > 1 })
        {
            var stats = ComputeStats(perCallMs);
            line += $" | per-call p50={stats.P50:F1}ms p95={stats.P95:F1}ms p99={stats.P99:F1}ms max={stats.Max:F1}ms";
        }
        outputHelper.WriteLine(line);
    }

    [Fact]
    public async Task Perf_Write_BulkBatch_Vs_IndividualSaveChanges()
    {
        const int count = 200;

        var bulkBlogs = Enumerable.Range(0, count)
            .Select(i => new BloggingFixture.Blog { BlogId = WriteBulkBlogIdBase + i, Url = $"http://perf.example.com/bulk/{i}" })
            .ToList();
        var individualBlogs = Enumerable.Range(0, count)
            .Select(i => new BloggingFixture.Blog { BlogId = WriteIndividualBlogIdBase + i, Url = $"http://perf.example.com/individual/{i}" })
            .ToList();

        // The whole test is one try/finally: SaveChangesAsync can throw partway through either
        // phase (some docs already written), and cleanup must still run in that case or the
        // shared fixture bucket is left with leaked docs that can cause duplicate-key/count
        // failures in later runs.
        try
        {
            // Bulk: all N adds staged in one context, one SaveChangesAsync call — hits
            // CouchbaseDatabaseWrapper's bounded-concurrency parallel write path.
            double bulkElapsedMs;
            await using (var ctx = bloggingFixture.GetDbContext())
            {
                ctx.AddRange(bulkBlogs);
                var sw = Stopwatch.StartNew();
                await ctx.SaveChangesAsync();
                sw.Stop();
                bulkElapsedMs = sw.Elapsed.TotalMilliseconds;
            }
            Report("Bulk insert (1 SaveChangesAsync, parallel write path)", count, bulkElapsedMs);

            // Individual: N separate SaveChangesAsync calls, one doc each — N sequential round
            // trips, the baseline the parallel batch path is meant to beat. Reuse one DbContext
            // across the loop (clearing tracking between iterations) rather than constructing a
            // fresh one per doc — a per-iteration DbContext would let its own construction/
            // disposal cost dominate the baseline, making the comparison less representative of
            // "N sequential SaveChangesAsync round trips" and potentially masking a real
            // regression in the provider's write path behind that fixed cost.
            var perCallMs = new List<double>(count);
            double individualTotalMs;
            await using (var ctx = bloggingFixture.GetDbContext())
            {
                var totalSw = Stopwatch.StartNew();
                foreach (var blog in individualBlogs)
                {
                    ctx.Add(blog);
                    var sw = Stopwatch.StartNew();
                    await ctx.SaveChangesAsync();
                    sw.Stop();
                    perCallMs.Add(sw.Elapsed.TotalMilliseconds);
                    ctx.ChangeTracker.Clear();
                }
                totalSw.Stop();
                individualTotalMs = totalSw.Elapsed.TotalMilliseconds;
            }
            Report("Individual insert (N SaveChangesAsync calls)", count, individualTotalMs, perCallMs);

            // The meaningful regression guard: batching+parallelizing N writes into one
            // SaveChanges must clearly beat doing them as N sequential round trips. Compare
            // against the summed per-call SaveChangesAsync time, not individualTotalMs — that
            // also includes loop overhead, which would let a real regression in the provider's
            // parallel write path hide behind that fixed cost instead of failing the assertion.
            var individualWriteOnlyMs = perCallMs.Sum();
            Assert.True(bulkElapsedMs < individualWriteOnlyMs,
                $"Expected bulk batch ({bulkElapsedMs:F0} ms) to beat individual sequential writes ({individualWriteOnlyMs:F0} ms).");
        }
        finally
        {
            // Best-effort cleanup: unconditionally RemoveRange-ing the full candidate lists would
            // itself throw if SaveChangesAsync failed partway through a batch (some keys never
            // written), masking the real failure. FindAsync each expected key and only remove
            // what's actually there.
            await using var ctx = bloggingFixture.GetDbContext();
            await RemoveExistingAsync(ctx, bulkBlogs, b => b.BlogId);
            await RemoveExistingAsync(ctx, individualBlogs, b => b.BlogId);
        }
    }

    // Best-effort cleanup shared by every scenario below: unconditionally RemoveRange-ing a full
    // candidate list would itself throw if seeding/measurement failed partway through (some keys
    // never written), masking the real failure and leaving the rest of the candidates leaked in
    // the shared fixture bucket. FindAsync each expected key and only remove what's actually there.
    private static async Task RemoveExistingAsync<TEntity>(DbContext ctx, List<TEntity> candidates, Func<TEntity, object> keySelector)
        where TEntity : class
    {
        var existing = new List<TEntity>();
        foreach (var candidate in candidates)
        {
            var found = await ctx.Set<TEntity>().FindAsync(keySelector(candidate));
            if (found != null)
            {
                existing.Add(found);
            }
        }
        if (existing.Count > 0)
        {
            ctx.Set<TEntity>().RemoveRange(existing);
            await ctx.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Perf_Read_BulkQuery_Vs_IndividualFind()
    {
        const int count = 100;

        var blogs = Enumerable.Range(0, count)
            .Select(i => new BloggingFixture.Blog { BlogId = ReadBulkBlogIdBase + i, Url = $"http://perf.example.com/read/{i}" })
            .ToList();

        try
        {
            await using (var seed = bloggingFixture.GetDbContext())
            {
                seed.AddRange(blogs);
                await seed.SaveChangesAsync();
            }

            // Bulk: a single N1QL round trip returning all N docs. Even under RequestPlus scan
            // consistency, a query issued immediately after a 100-way concurrent write batch can
            // rarely observe a stale index under heavy cluster load (confirmed empirically: 1 flake
            // in ~5 full-suite runs, always resolving on an immediate retry) — poll like the
            // transaction tests do for the same class of transient staleness, rather than asserting
            // on a single attempt. The poll itself is untimed (its whole point is to absorb
            // consistency-wait latency, not have it counted as query latency); only the final,
            // already-consistent attempt is timed. ChangeTracker.Clear() drops the entities the
            // successful poll attempt tracked, so the timed query does full materialization work
            // rather than partly short-circuiting via identity resolution on already-tracked rows.
            double bulkElapsedMs;
            await using (var ctx = bloggingFixture.GetDbContext())
            {
                var lowerBound = ReadBulkBlogIdBase;
                var upperBound = lowerBound + count;
                await PollingHelper.PollForResultAsync(
                    () => ctx.Blogs.Where(b => b.BlogId >= lowerBound && b.BlogId < upperBound).ToListAsync(),
                    r => r.Count == count,
                    TimeSpan.FromSeconds(5));
                ctx.ChangeTracker.Clear();

                var sw = Stopwatch.StartNew();
                var results = await ctx.Blogs.Where(b => b.BlogId >= lowerBound && b.BlogId < upperBound).ToListAsync();
                sw.Stop();
                bulkElapsedMs = sw.Elapsed.TotalMilliseconds;
                Assert.Equal(count, results.Count);
            }
            Report("Bulk query (1 round trip)", count, bulkElapsedMs);

            // Individual: N separate KV point reads (FindAsync).
            var perCallMs = new List<double>(count);
            await using (var ctx = bloggingFixture.GetDbContext())
            {
                var totalSw = Stopwatch.StartNew();
                foreach (var blog in blogs)
                {
                    var sw = Stopwatch.StartNew();
                    var found = await ctx.Blogs.FindAsync(blog.BlogId);
                    sw.Stop();
                    Assert.NotNull(found);
                    perCallMs.Add(sw.Elapsed.TotalMilliseconds);
                }
                totalSw.Stop();
                Report("Individual KV point reads (N FindAsync calls)", count, totalSw.Elapsed.TotalMilliseconds, perCallMs);

                // A single-round-trip bulk query must clearly beat N sequential point reads.
                // Compare against the summed per-call FindAsync time, not totalSw — totalSw also
                // includes loop overhead and the per-iteration Assert.NotNull, which would let a
                // real regression hide behind that fixed cost instead of failing the assertion.
                var individualReadOnlyMs = perCallMs.Sum();
                Assert.True(bulkElapsedMs < individualReadOnlyMs,
                    $"Expected bulk query ({bulkElapsedMs:F0} ms) to beat individual point reads ({individualReadOnlyMs:F0} ms).");
            }
        }
        finally
        {
            await using var cleanup = bloggingFixture.GetDbContext();
            await RemoveExistingAsync(cleanup, blogs, b => b.BlogId);
        }
    }

    [Fact]
    public async Task Perf_Read_OwnedTypeOverhead_Vs_PlainEntity()
    {
        const int count = 50;

        var customers = Enumerable.Range(0, count).Select(i => new OwnedTypeFixture.Customer
        {
            CustomerId = CustomerIdBase + i,
            Name = $"PerfCustomer{i}",
            Address = new OwnedTypeFixture.Address { Street = $"{i} Perf St", City = "Loadtestville" },
            ContactMethods =
            [
                new OwnedTypeFixture.ContactMethod { Id = 1, Type = "email", Value = $"perf{i}@example.com" },
                new OwnedTypeFixture.ContactMethod { Id = 2, Type = "phone", Value = $"555-{i:D4}" }
            ]
        }).ToList();

        var plainBlogs = Enumerable.Range(0, count)
            .Select(i => new BloggingFixture.Blog { BlogId = OwnedComparisonPlainBlogIdBase + i, Url = $"http://perf.example.com/plain/{i}" })
            .ToList();

        try
        {
            await using (var seed = ownedTypeFixture.GetDbContext())
            {
                seed.AddRange(customers);
                await seed.SaveChangesAsync();
            }
            await using (var seed = bloggingFixture.GetDbContext())
            {
                seed.AddRange(plainBlogs);
                await seed.SaveChangesAsync();
            }

            // See the read-throughput scenario above for why this polls rather than asserting on a
            // single attempt: a query immediately after a bulk concurrent write can rarely observe
            // a stale index under heavy cluster load even under RequestPlus scan consistency. The
            // poll is untimed and its tracked entities are cleared before the timed attempt, for
            // the same reason as above — otherwise the logged numbers (and the owned-vs-plain
            // comparison) would be skewed by however long consistency-wait happened to take, and by
            // identity-resolution short-circuiting on rows the poll already tracked.
            double ownedElapsedMs;
            await using (var ctx = ownedTypeFixture.GetDbContext())
            {
                var lowerBound = CustomerIdBase;
                var upperBound = CustomerIdBase + count;
                await PollingHelper.PollForResultAsync(
                    () => ctx.Customers.Where(c => c.CustomerId >= lowerBound && c.CustomerId < upperBound).ToListAsync(),
                    r => r.Count == count,
                    TimeSpan.FromSeconds(5));
                ctx.ChangeTracker.Clear();

                var sw = Stopwatch.StartNew();
                var results = await ctx.Customers.Where(c => c.CustomerId >= lowerBound && c.CustomerId < upperBound).ToListAsync();
                sw.Stop();
                ownedElapsedMs = sw.Elapsed.TotalMilliseconds;
                Assert.Equal(count, results.Count);
                Assert.All(results, c => Assert.Equal(2, c.ContactMethods.Count));
            }
            Report("Owned-type bulk query (Customer + Address + ContactMethods)", count, ownedElapsedMs);

            double plainElapsedMs;
            await using (var ctx = bloggingFixture.GetDbContext())
            {
                var lowerBound = OwnedComparisonPlainBlogIdBase;
                var upperBound = lowerBound + count;
                await PollingHelper.PollForResultAsync(
                    () => ctx.Blogs.Where(b => b.BlogId >= lowerBound && b.BlogId < upperBound).ToListAsync(),
                    r => r.Count == count,
                    TimeSpan.FromSeconds(5));
                ctx.ChangeTracker.Clear();

                var sw = Stopwatch.StartNew();
                var results = await ctx.Blogs.Where(b => b.BlogId >= lowerBound && b.BlogId < upperBound).ToListAsync();
                sw.Stop();
                plainElapsedMs = sw.Elapsed.TotalMilliseconds;
                Assert.Equal(count, results.Count);
            }
            Report("Plain-entity bulk query (Blog, no owned navs)", count, plainElapsedMs);

            var overheadPct = (ownedElapsedMs / plainElapsedMs - 1.0) * 100;
            outputHelper.WriteLine($"Owned-type reflection-materialization overhead vs plain entity: {overheadPct:F1}%");
        }
        finally
        {
            await using var cleanupOwned = ownedTypeFixture.GetDbContext();
            await RemoveExistingAsync(cleanupOwned, customers, c => c.CustomerId);

            await using var cleanupPlain = bloggingFixture.GetDbContext();
            await RemoveExistingAsync(cleanupPlain, plainBlogs, b => b.BlogId);
        }
    }
}
