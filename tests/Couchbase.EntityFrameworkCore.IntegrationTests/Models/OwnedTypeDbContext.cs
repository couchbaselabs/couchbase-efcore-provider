using Couchbase.EntityFrameworkCode.IntegrationTests.Fixtures;
using Couchbase.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Couchbase.EntityFrameworkCode.IntegrationTests.Models;

public class OwnedTypeDbContext(DbContextOptions<OwnedTypeDbContext> options) : DbContext(options)
{
    public DbSet<OwnedTypeFixture.Customer> Customers { get; set; }

    /// <summary>
    /// HashSet&lt;T&gt;-backed entity — exercises the ICollection&lt;T&gt; fallback clear path
    /// in MaterializeOwnedItem (the non-IList branch).
    /// </summary>
    public DbSet<OwnedTypeFixture.HashSetCustomer> HashSetCustomers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OwnedTypeFixture.Customer>(b =>
        {
            b.ToCouchbaseCollection(this, "customer");
            b.OwnsOne(c => c.Address);
            b.OwnsMany(c => c.ContactMethods, cm =>
            {
                cm.OwnsOne(m => m.Label);
                cm.OwnsMany(m => m.Tags, t =>
                {
                    t.OwnsMany(t => t.Audits);
                });
            });
        });

        modelBuilder.Entity<OwnedTypeFixture.HashSetCustomer>(b =>
        {
            b.ToCouchbaseCollection(this, "hashsetcustomer");
            b.HasKey(c => c.Id);
            b.OwnsMany(c => c.Tags, t => t.HasKey(t => t.Id));
        });

        modelBuilder.ConfigureToCouchbase(this, true);
    }
}
