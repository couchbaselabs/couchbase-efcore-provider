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

    /// <summary>
    /// Get-only OwnsOne / OwnsMany + HashSet nested collection —
    /// exercises FieldInfo fallback and ICollection&lt;T&gt; nested clear.
    /// </summary>
    public DbSet<OwnedTypeFixture.FieldAccessCustomer> FieldAccessCustomers { get; set; }

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

        modelBuilder.Entity<OwnedTypeFixture.FieldAccessCustomer>(b =>
        {
            b.ToCouchbaseCollection(this, "fieldaccesscustomer");
            b.HasKey(c => c.Id);
            // Use field access so EF reads/writes get-only properties via backing fields.
            b.OwnsOne(c => c.Address, a =>
            {
                a.Property(x => x.Street).UsePropertyAccessMode(PropertyAccessMode.Field);
                a.Property(x => x.City).UsePropertyAccessMode(PropertyAccessMode.Field);
            });
            b.OwnsMany(c => c.Contacts, cm =>
            {
                cm.HasKey(c => c.Id);
                cm.Property(c => c.Label).UsePropertyAccessMode(PropertyAccessMode.Field);
                cm.OwnsMany(c => c.Tags, t => t.HasKey(t => t.Id));
            });
        });

        modelBuilder.ConfigureToCouchbase(this, true);
    }
}
