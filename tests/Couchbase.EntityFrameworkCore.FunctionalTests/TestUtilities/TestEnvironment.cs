using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Couchbase.EntityFrameworkCore.FunctionalTests.TestUtilities;

public static class TestEnvironment
{
    public static IConfiguration Config { get; } = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("config.json", optional: true)
        .AddJsonFile("config.test.json", optional: true)
        .AddEnvironmentVariables()
        .Build()
        .GetSection("Test:Couchbase");

    public static string DefaultConnection { get; } = string.IsNullOrEmpty(Config["DefaultConnection"])
        ? ConnectionString
        : Config["DefaultConnection"];

    public static string ConnectionString { get; } = "couchbase://localhost";

    public static string BucketName { get; } = Config["BucketName"];

    public static string Password { get; } = Config["Password"];

    public static string Username { get; } = Config["Username"];

    public static string Scope { get; } = Config["Scope"];
}