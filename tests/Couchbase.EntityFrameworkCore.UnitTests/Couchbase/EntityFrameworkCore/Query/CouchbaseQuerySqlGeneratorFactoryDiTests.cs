using System.Text.Json;
using Couchbase.EntityFrameworkCore.Extensions;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// <see cref="Couchbase.EntityFrameworkCore.Query.Internal.CouchbaseQuerySqlGeneratorFactory"/>
/// used to have a commented-out, never-finished <c>ICouchbaseDbContextOptionsBuilder</c>
/// constructor parameter -- constructor-injecting it directly would have been a captive-dependency
/// bug, since <c>IQuerySqlGeneratorFactory</c> is Singleton-lifetime (EF Core's own registration)
/// while <c>ICouchbaseDbContextOptionsBuilder</c> is Scoped. This guards the fix (capturing
/// <c>FieldNamingPolicy</c> by value at DI-registration time instead).
/// </summary>
public class CouchbaseQuerySqlGeneratorFactoryDiTests
{
    private class Post
    {
        public int PostId { get; set; }
        public string Title { get; set; } = "";
    }

    private class PostContext(DbContextOptions<PostContext> options) : DbContext(options)
    {
        public DbSet<Post> Posts { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Post>(b =>
            {
                b.ToCouchbaseCollection("bucket", "scope", "post");
                b.HasKey(p => p.PostId);
            });
        }
    }

    private static DbContextOptionsBuilder<PostContext> CreateBuilder(JsonNamingPolicy? policy)
    {
        var clusterOptions = new ClusterOptions()
            .WithConnectionString("couchbase://localhost")
            .WithPasswordAuthentication("Administrator", "password");

        var builder = new DbContextOptionsBuilder<PostContext>();
        builder.UseCouchbaseProvider(clusterOptions, o => o.FieldNamingPolicy = policy);
        return builder;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("CamelCase")]
    [InlineData("SnakeCaseLower")]
    public void QueryCompiles_RegardlessOfConfiguredFieldNamingPolicy(string? policyName)
    {
        JsonNamingPolicy? policy = policyName switch
        {
            "CamelCase" => JsonNamingPolicy.CamelCase,
            "SnakeCaseLower" => JsonNamingPolicy.SnakeCaseLower,
            _ => null,
        };

        using var ctx = new PostContext(CreateBuilder(policy).Options);

        var sql = ctx.Posts.Where(p => p.Title == "x").ToQueryString();

        Assert.Contains("`b`.`Title` = 'x'", sql);
    }

    /// <summary>
    /// EF Core builds its own internal service provider with <c>ValidateScopes</c>/
    /// <c>ValidateOnBuild</c> both off (<c>ServiceProviderCache.BuildServiceProvider</c> calls the
    /// parameterless <c>services.BuildServiceProvider()</c>), so exercising a query through a
    /// normal <see cref="DbContext"/> — as <see cref="QueryCompiles_RegardlessOfConfiguredFieldNamingPolicy"/>
    /// does — would silently succeed even if the captive-dependency bug were reintroduced; a real
    /// captive-dependency exception only surfaces under explicit scope validation. This test builds
    /// the exact same registrations
    /// (<see cref="CouchbaseOptionsExtension.ApplyServices"/>, the same method EF Core's own
    /// provider-building calls) into a fresh <see cref="ServiceCollection"/> and resolves
    /// <see cref="IQuerySqlGeneratorFactory"/> from an explicitly scope-validating provider, so a
    /// reintroduced constructor-injected <c>ICouchbaseDbContextOptionsBuilder</c> would fail this
    /// test with <see cref="InvalidOperationException"/> rather than passing silently.
    /// </summary>
    [Fact]
    public void QuerySqlGeneratorFactory_ResolvesUnderScopeValidation_WithoutCaptiveDependencyException()
    {
        var options = CreateBuilder(JsonNamingPolicy.CamelCase).Options;
        var extension = options.FindExtension<CouchbaseOptionsExtension>();
        Assert.NotNull(extension);

        var services = new ServiceCollection();
        extension.ApplyServices(services);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var factory = scope.ServiceProvider.GetRequiredService<IQuerySqlGeneratorFactory>();

        Assert.NotNull(factory);
    }
}
