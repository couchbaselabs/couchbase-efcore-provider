using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Models;

public class OwnedTypeDbContext(DbContextOptions<OwnedTypeDbContext> options) : DbContext(options)
{
    public DbSet<OwnedTypeFixture.Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OwnedTypeFixture.Customer>(b =>
        {
            b.ToCouchbaseCollection(this, "customer");
            b.OwnsOne(c => c.Address);
            b.OwnsMany(c => c.ContactMethods, cm =>
            {
                cm.OwnsOne(m => m.Label);
                cm.OwnsMany(m => m.Tags);
            });
        });

        modelBuilder.ConfigureToCouchbase(this, true);
    }
}
