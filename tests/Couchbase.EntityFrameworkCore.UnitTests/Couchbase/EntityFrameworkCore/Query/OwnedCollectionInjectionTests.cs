using System.Text.Json;
using Couchbase.EntityFrameworkCore.Query.Internal;
using Xunit;

namespace Couchbase.EntityFrameworkCore.UnitTests.Couchbase.EntityFrameworkCore.Query;

/// <summary>
/// Tests for the SQL injection logic that adds OwnsMany navigation columns to
/// EF Core-generated N1QL queries without N+1 KV GETs.
/// </summary>
public class OwnedCollectionInjectionTests
{
    // Realistic multi-line SQL EF Core generates for FirstAsync with OwnsMany.
    // AppendLine() + Append("FROM ") produces \nFROM (not space+FROM).
    private const string FirstAsyncSql =
        "SELECT `c`.`customerId`, `c`.`name`, `c`.`address_Street`, `c`.`address_City`, `c0`.`id`, `c0`.`type`, `c0`.`value`, `c0`.`customerId`\n" +
        "FROM `default`.`ownedtypes`.`customer` AS `c`\n" +
        "LEFT JOIN `Customer_ContactMethods` AS `c0` ON `c`.`customerId` = `c0`.`customerId`\n" +
        "WHERE `c`.`customerId` = $__p_0\n" +
        "ORDER BY `c`.`customerId`\n" +
        "LIMIT 1";

    private const string ToListAsyncSql =
        "SELECT `c`.`customerId`, `c`.`name`, `c`.`address_Street`, `c`.`address_City`, `c0`.`id`, `c0`.`type`, `c0`.`value`, `c0`.`customerId`\n" +
        "FROM `default`.`ownedtypes`.`customer` AS `c`\n" +
        "LEFT JOIN `Customer_ContactMethods` AS `c0` ON `c`.`customerId` = `c0`.`customerId`\n" +
        "ORDER BY `c`.`customerId`";

    private const string TableName = "default.ownedtypes.customer";
    private static readonly string[] NavNames = ["contactMethods"];

    [Fact]
    public void IndexOfFrom_finds_newline_FROM()
    {
        var idx = CouchbaseQueryEnumerable<object>.IndexOfFrom(FirstAsyncSql);
        Assert.True(idx >= 0, "\\nFROM not found");
        Assert.Equal('\n', FirstAsyncSql[idx]);
    }

    [Fact]
    public void IndexOfFrom_finds_space_FROM_in_single_line_sql()
    {
        const string sql = "SELECT `c`.`id` FROM `default`.`s`.`col` AS `c`";
        var idx = CouchbaseQueryEnumerable<object>.IndexOfFrom(sql);
        Assert.True(idx >= 0, "space+FROM not found");
        Assert.Equal(' ', sql[idx]);
    }

    [Fact]
    public void ExtractSelectAlias_returns_correct_alias_for_multiline_sql()
    {
        var alias = CouchbaseQueryEnumerable<object>.ExtractSelectAlias(FirstAsyncSql, TableName);
        Assert.Equal("c", alias);
    }

    [Fact]
    public void ExtractSelectAlias_fallback_works_when_keyspace_parse_fails()
    {
        const string sql =
            "SELECT `c`.`id`\n" +
            "FROM `default`.`s`.`col` AS `c`\n" +
            "WHERE `c`.`id` = 1";
        // Pass null tableName to force fallback path
        var alias = CouchbaseQueryEnumerable<object>.ExtractSelectAlias(sql, null);
        Assert.Equal("c", alias);
    }

    [Fact]
    public void InjectOwnedCollectionColumns_appends_nav_column_before_FROM()
    {
        var result = CouchbaseQueryEnumerable<object>.InjectOwnedCollectionColumns(
            FirstAsyncSql, TableName, NavNames);

        Assert.Contains("`c`.`contactMethods`", result);
        // The injection must appear before \nFROM
        var injIdx = result.IndexOf("`c`.`contactMethods`", StringComparison.Ordinal);
        var fromIdx = result.IndexOf("\nFROM", StringComparison.OrdinalIgnoreCase);
        Assert.True(injIdx < fromIdx, "injected column must come before FROM clause");
        // FROM clause and rest of query must be preserved intact
        Assert.Contains("FROM `default`.`ownedtypes`.`customer` AS `c`", result);
        Assert.Contains("LIMIT 1", result);
    }

    [Fact]
    public void InjectOwnedCollectionColumns_works_for_ToListAsync_sql()
    {
        var result = CouchbaseQueryEnumerable<object>.InjectOwnedCollectionColumns(
            ToListAsyncSql, TableName, NavNames);

        Assert.Contains("`c`.`contactMethods`", result);
        Assert.Contains("ORDER BY `c`.`customerId`", result);
    }

    // EF Core wraps the limited query in a subquery when FirstAsync/Take is used.
    private const string FirstAsyncSubquerySql =
        "SELECT `d0`.`customerId`, `d0`.`name`, `d0`.`address_City`, `d0`.`address_Street`, `c`.`id`, `c`.`type`, `c`.`value`\n" +
        "FROM (\n" +
        "    SELECT `d`.`customerId`, `d`.`name`, `d`.`address_City`, `d`.`address_Street`\n" +
        "    FROM `default`.`ownedtypes`.`customer` AS `d`\n" +
        "    WHERE `d`.`customerId` = 1\n" +
        "    LIMIT 1\n" +
        ") AS `d0`\n" +
        "\n" +
        "ORDER BY `d0`.`customerId`, `c`.`customerId`";

    [Fact]
    public void InjectOwnedCollectionColumns_handles_subquery_form_FirstAsync()
    {
        var result = CouchbaseQueryEnumerable<object>.InjectOwnedCollectionColumns(
            FirstAsyncSubquerySql, TableName, ["contactMethods"]);

        // Outer SELECT must have `d0`.`contactMethods`
        Assert.Contains("`d0`.`contactMethods`", result);
        // Inner subquery SELECT must have `d`.`contactMethods`
        Assert.Contains("`d`.`contactMethods`", result);
        // The outer FROM clause must still be intact
        Assert.Contains("FROM (", result);
        Assert.Contains(") AS `d0`", result);
        // ORDER BY must be preserved
        Assert.Contains("ORDER BY `d0`.`customerId`", result);
    }

    [Fact]
    public void InjectOwnedCollectionColumns_throws_when_FROM_not_found()
    {
        const string broken = "SELECT `c`.`id` WHERE `c`.`id` = 1"; // no FROM
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CouchbaseQueryEnumerable<object>.InjectOwnedCollectionColumns(broken, TableName, NavNames));
        Assert.Contains("no FROM clause found", ex.Message);
    }

    [Fact]
    public void InjectOwnedCollectionColumns_throws_when_subquery_parens_unbalanced()
    {
        // FROM starts a subquery but the closing ')' is missing.
        const string broken =
            "SELECT `d0`.`id`\n" +
            "FROM (\n" +
            "    SELECT `d`.`id`\n" +
            "    FROM `default`.`s`.`col` AS `d`\n";
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CouchbaseQueryEnumerable<object>.InjectOwnedCollectionColumns(broken, TableName, NavNames));
        Assert.Contains("unbalanced parentheses", ex.Message);
    }

    [Fact]
    public void InjectOwnedCollectionColumns_throws_when_outer_alias_missing()
    {
        // Subquery closes but has no AS `alias` afterward.
        const string broken =
            "SELECT `d0`.`id`\n" +
            "FROM (\n" +
            "    SELECT `d`.`id`\n" +
            "    FROM `default`.`s`.`col` AS `d`\n" +
            ")\n" +
            "ORDER BY 1";
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CouchbaseQueryEnumerable<object>.InjectOwnedCollectionColumns(broken, TableName, NavNames));
        Assert.Contains("outer alias", ex.Message);
    }

    [Fact]
    public void InjectOwnedCollectionColumns_throws_when_simple_form_alias_missing()
    {
        // FROM clause exists but has no AS `alias` after the keyspace.
        const string broken = "SELECT `c`.`id`\nFROM `default`.`s`.`col`\nWHERE 1=1";
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CouchbaseQueryEnumerable<object>.InjectOwnedCollectionColumns(broken, TableName, NavNames));
        Assert.Contains("table alias", ex.Message);
    }

    [Fact]
    public void TryGetPropertyCI_finds_camelCase_property_case_insensitively()
    {
        var json = JsonDocument.Parse(
            """{"contactMethods":[{"id":1,"type":"email","value":"a@b.com"}]}""");

        // Search with PascalCase (as EF Core navigation name)
        var found = CouchbaseQueryEnumerable<object>.TryGetPropertyCI(
            json.RootElement, "ContactMethods", out var val);

        Assert.True(found);
        Assert.Equal(JsonValueKind.Array, val.ValueKind);
        Assert.Equal(1, val.GetArrayLength());
    }
}
