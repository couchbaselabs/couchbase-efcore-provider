namespace Couchbase.EntityFrameworkCore.FunctionalTests.Models;

public class Session
{
    public Guid Id { get; set; }
    public string Category { get; set; }

    public string TenantId { get; set; } = null!;
    public Guid UserId { get; set; }
    public int SessionId { get; set; }
}