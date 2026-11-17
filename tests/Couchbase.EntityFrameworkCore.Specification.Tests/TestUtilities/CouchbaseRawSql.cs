using System.Text.RegularExpressions;

namespace Couchbase.EntityFrameworkCore.SpecificationTests.TestUtilities;

/// <summary>
/// Test-harness only: rewrites the T-SQL that EF's raw-SQL suites embed (FromSql, SqlQuery,
/// ExecuteSql) into the SQL++ a Couchbase user would write for the same query. Relational
/// providers only swap delimiters; SQL++ differs in three more ways, rewritten here so the suites
/// measure the provider rather than the dialect:
/// <list type="bullet">
/// <item>Delimiters: <c>[Customers]</c> becomes <c>`Customers`</c> (spaces become underscores, as
/// in the collection names the spec model uses).</item>
/// <item>Keyspaces: the provider runs queries at cluster level, so a bare collection after FROM or
/// JOIN is qualified with the test bucket and scope, and given an alias if it has none.</item>
/// <item><c>SELECT *</c>: in SQL++ it wraps each row in an object named after the keyspace
/// (<c>{"Customers": {...}}</c>), so it becomes <c>SELECT `Customers`.*</c>.</item>
/// <item>Named parameters: <c>@city</c> becomes <c>$city</c>.</item>
/// </list>
/// Whether the provider itself should accept bare collection names (query_context) or unwrap
/// <c>SELECT *</c> is an open product question.
/// </summary>
public static class CouchbaseRawSql
{
    private static readonly Regex Brackets = new(@"\[([^\]]+)\]");
    private static readonly Regex CteNames = new(@"\bWITH\s+`([^`]+)`\s+AS\b|,\s*`([^`]+)`\s+AS\s*\(", RegexOptions.IgnoreCase);
    private static readonly Regex Keyspace = new(
        @"\b(FROM|JOIN)(\s+)`([^`]+)`(?!\s*\.)(\s+AS\s+`[^`]+`)?",
        RegexOptions.IgnoreCase);
    private static readonly Regex SelectStar = new(
        @"\bSELECT(\s+)\*(\s+FROM\s+(?:`[^`]+`\.)*`([^`]+)`(?:\s+AS\s+`([^`]+)`)?)",
        RegexOptions.IgnoreCase);
    private static readonly Regex NamedParameter = new(@"(?<![\w@'$])@(\w+)");

    public static string Normalize(string sql, string bucket, string scope)
    {
        sql = Brackets.Replace(sql, m => "`" + m.Groups[1].Value.Replace(' ', '_') + "`");

        var ctes = CteNames.Matches(sql)
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        sql = Keyspace.Replace(sql, m =>
        {
            var name = m.Groups[3].Value;
            if (ctes.Contains(name)) return m.Value;
            var alias = m.Groups[4].Success ? m.Groups[4].Value : $" AS `{name}`";
            return $"{m.Groups[1].Value}{m.Groups[2].Value}`{bucket}`.`{scope}`.`{name}`{alias}";
        });

        sql = SelectStar.Replace(sql, m =>
        {
            var alias = m.Groups[4].Success ? m.Groups[4].Value : m.Groups[3].Value;
            return $"SELECT{m.Groups[1].Value}`{alias}`.*{m.Groups[2].Value}";
        });

        return NamedParameter.Replace(sql, "$$$1");
    }

    /// <summary>EF's suites name DbParameters "@city"; SQL++ named parameters have no prefix.</summary>
    public static string? ParameterName(string? name) => name?.TrimStart('@', '$');
}
