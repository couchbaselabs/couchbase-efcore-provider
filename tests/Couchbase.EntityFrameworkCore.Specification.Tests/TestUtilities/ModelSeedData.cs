using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;

/// <summary>
/// Inserts the data a model declares with <c>HasData</c>.
/// </summary>
/// <remarks>
/// EF Core's relational <c>EnsureCreated</c> inserts model seed data, but the Couchbase provider's
/// <c>EnsureCreatedAsync</c> does not, so spec fixtures that seed through <c>HasData</c> would
/// otherwise find an empty database. This is a stand-in for that, in the test project only.
/// </remarks>
public static class ModelSeedData
{
    public static async Task InsertAsync(DbContext context)
    {
        // The runtime model has its seed data stripped; the design-time model keeps it.
        var designTimeModel = context.GetService<IDesignTimeModel>().Model;

        var any = false;
        foreach (var entityType in designTimeModel.GetEntityTypes().Where(e => !e.IsOwned()))
        {
            foreach (var data in entityType.GetSeedData())
            {
                var entity = Activator.CreateInstance(entityType.ClrType, nonPublic: true)!;
                var entry = context.Entry(entity);
                entry.CurrentValues.SetValues(data.ToDictionary(kv => kv.Key, kv => kv.Value!));
                entry.State = EntityState.Added;
                any = true;
            }
        }

        if (any)
        {
            await context.SaveChangesAsync();
        }
    }
}
