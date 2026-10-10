using System.Text.RegularExpressions;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

internal enum SavepointOperation
{
    Create,
    RollbackTo,
    Release
}

/// <summary>
/// The statements EF Core's <c>RelationalTransaction</c> sends for savepoints, and the parser that
/// recognises them again in <see cref="CouchbaseCommand"/>.
/// </summary>
/// <remarks>
/// Couchbase SQL++ only accepts <c>SAVEPOINT</c> inside a server-side <c>BEGIN WORK</c>
/// transaction, but this provider buffers a transaction's mutations client-side and commits them
/// atomically through the SDK (see <see cref="CouchbaseDbTransaction"/>), so there is no server-side
/// transaction for such a statement to run in. Savepoints are instead resolved client-side against
/// that buffer. Generating the text here and parsing it in one place keeps EF's own savepoint flow
/// (interceptors, logging, suppression) intact without subclassing <c>RelationalTransaction</c>.
/// </remarks>
internal static partial class CouchbaseSavepointStatements
{
    public static string Create(string delimitedName) => $"SAVEPOINT {delimitedName}";

    public static string RollbackTo(string delimitedName) => $"ROLLBACK TO SAVEPOINT {delimitedName}";

    public static string Release(string delimitedName) => $"RELEASE SAVEPOINT {delimitedName}";

    /// <summary>
    /// Parses a statement produced by this class. Returns <c>false</c> for anything else (including
    /// ordinary queries), which the caller then sends to the server unchanged.
    /// </summary>
    public static bool TryParse(string? commandText, out SavepointOperation operation, out string name)
    {
        operation = default;
        name = string.Empty;

        if (string.IsNullOrWhiteSpace(commandText))
        {
            return false;
        }

        var match = Pattern().Match(commandText);
        if (!match.Success)
        {
            return false;
        }

        operation = match.Groups["rollback"].Success ? SavepointOperation.RollbackTo
            : match.Groups["release"].Success ? SavepointOperation.Release
            : SavepointOperation.Create;
        name = match.Groups["name"].Value.Replace("``", "`");
        return true;
    }

    [GeneratedRegex(
        @"^\s*(?:(?<rollback>ROLLBACK\s+TO\s+SAVEPOINT)|(?<release>RELEASE\s+SAVEPOINT)|SAVEPOINT)\s+`(?<name>(?:[^`]|``)+)`\s*;?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
