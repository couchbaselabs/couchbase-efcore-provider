using System.Data;
using System.Data.Common;
using System.Text.Json;
using Couchbase.Query;
using Couchbase.EntityFrameworkCore.Internal;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseCommand : DbCommand
{
    private CancellationTokenSource? _cancellationTokenSource;
    private string _commandText = string.Empty;

    public new virtual CouchbaseParameterCollection Parameters
        => field ??= new CouchbaseParameterCollection();

    internal ICluster? Cluster { get; set; }

    public override string CommandText
    {
        get => _commandText;
        set => _commandText = value ?? string.Empty;
    }

    public override int CommandTimeout { get; set; }

    public override CommandType CommandType { get; set; } = CommandType.Text;

    public override UpdateRowSource UpdatedRowSource { get; set; } = UpdateRowSource.None;

    protected override DbConnection? DbConnection { get; set; }

    protected override DbParameterCollection DbParameterCollection => Parameters;

    protected override DbTransaction? DbTransaction { get; set; }

    public override bool DesignTimeVisible { get; set; }

    public override void Cancel()
    {
        _cancellationTokenSource?.Cancel();
    }

    public override void Prepare()
    {
        // No-op: Couchbase N1QL auto-prepares queries.
        // The ADO.NET contract treats Prepare as a hint, not a requirement.
    }

    public override Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        // No-op for Couchbase
        return Task.CompletedTask;
    }

    public override int ExecuteNonQuery()
    {
        return AsyncHelper.RunSync(
            static state => state.ExecuteNonQueryAsync(CancellationToken.None),
            this);
    }

    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        using var linkedCts = CreateLinkedTokenSource(cancellationToken);
        var options = BuildQueryOptions(linkedCts.Token);

        var cluster = ResolveCluster();
        var result = await cluster.QueryAsync<object>(CommandText, options).ConfigureAwait(false);

        // Drain all rows to ensure query completes
        await foreach (var _ in result.Rows.WithCancellation(linkedCts.Token).ConfigureAwait(false))
        {
        }

        // Per ADO.NET spec: return rows affected for UPDATE/INSERT/DELETE, -1 for other statements
        if (!IsDmlStatement(CommandText))
        {
            return -1;
        }

        var metrics = result.MetaData?.Metrics;
        if (metrics != null)
        {
            var mutationCount = metrics.MutationCount;

            // ADO.NET ExecuteNonQuery returns int, so clamp to int.MaxValue if exceeded
            if (mutationCount > int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)mutationCount;
        }

        return -1;
    }

    public override object? ExecuteScalar()
    {
        return AsyncHelper.RunSync(
            static state => state.ExecuteScalarAsync(CancellationToken.None),
            this);
    }

    public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        using var linkedCts = CreateLinkedTokenSource(cancellationToken);
        var options = BuildQueryOptions(linkedCts.Token);

        var cluster = ResolveCluster();
        var result = await cluster.QueryAsync<JsonElement>(CommandText, options).ConfigureAwait(false);

        await foreach (var row in result.Rows.WithCancellation(linkedCts.Token).ConfigureAwait(false))
        {
            return ExtractScalarValue(row);
        }

        return null;
    }

    private static object? ExtractScalarValue(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return DBNull.Value;

            case JsonValueKind.True:
                return true;

            case JsonValueKind.False:
                return false;

            case JsonValueKind.String:
                return element.GetString();

            case JsonValueKind.Number:
                if (element.TryGetInt64(out var longVal))
                    return longVal;
                return element.GetDouble();

            case JsonValueKind.Object:
                // SELECT col AS x -> extract first property value
                using (var enumerator = element.EnumerateObject())
                {
                    if (enumerator.MoveNext())
                    {
                        return ExtractScalarValue(enumerator.Current.Value);
                    }
                }
                return null;

            case JsonValueKind.Array:
                // Return first element if array
                using (var enumerator = element.EnumerateArray())
                {
                    if (enumerator.MoveNext())
                    {
                        return ExtractScalarValue(enumerator.Current);
                    }
                }
                return null;

            default:
                return null;
        }
    }

    protected override DbParameter CreateDbParameter()
    {
        return new CouchbaseParameter();
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        return AsyncHelper.RunSync(
            static state => state.Command.ExecuteDbDataReaderAsync(state.Behavior, CancellationToken.None),
            (Command: this, Behavior: behavior));
    }

    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(
        CommandBehavior behavior,
        CancellationToken cancellationToken)
    {
        using var linkedCts = CreateLinkedTokenSource(cancellationToken);
        var options = BuildQueryOptions(linkedCts.Token);

        var cluster = ResolveCluster();
        var queryResult = await cluster.QueryAsync<JsonElement>(CommandText, options).ConfigureAwait(false);

        return new CouchbaseDbDataReader<JsonElement>(queryResult, DbConnection, behavior, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        await base.DisposeAsync().ConfigureAwait(false);
    }

    private ICluster ResolveCluster()
    {
        if (Cluster != null)
        {
            return Cluster;
        }

        if (DbConnection is CouchbaseConnection couchbaseConnection && couchbaseConnection.Cluster != null)
        {
            return couchbaseConnection.Cluster;
        }

        throw new InvalidOperationException(
            "No Couchbase cluster is available. Ensure the connection is open or the Cluster property is set.");
    }

    private QueryOptions BuildQueryOptions(CancellationToken cancellationToken)
    {
        var options = new QueryOptions().CancellationToken(cancellationToken);

        if (CommandTimeout > 0)
        {
            options.Timeout(TimeSpan.FromSeconds(CommandTimeout));
        }

        foreach (var dbParameter in Parameters)
        {
            if (dbParameter is CouchbaseParameter parameter)
            {
                if (string.IsNullOrEmpty(parameter.ParameterName))
                {
                    throw new InvalidOperationException(
                        "Parameter name cannot be null or empty. Use AddWithValue or set ParameterName before executing.");
                }

                // Convert DBNull.Value to null for Couchbase SDK compatibility
                var value = parameter.Value == DBNull.Value ? null : parameter.Value;
                options.Parameter(parameter.ParameterName, value);
            }
        }

        return options;
    }

    private CancellationTokenSource CreateLinkedTokenSource(CancellationToken externalToken)
    {
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();

        return externalToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, externalToken)
            : CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token);
    }

    private static bool IsDmlStatement(string commandText)
    {
        var trimmed = commandText.AsSpan().TrimStart();
        return trimmed.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("UPSERT", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("MERGE", StringComparison.OrdinalIgnoreCase);
    }
}

/* ************************************************************
 *
 *    @author Couchbase <info@couchbase.com>
 *    @copyright 2025 Couchbase, Inc.
 *
 *    Licensed under the Apache License, Version 2.0 (the "License");
 *    you may not use this file except in compliance with the License.
 *    You may obtain a copy of the License at
 *
 *        http://www.apache.org/licenses/LICENSE-2.0
 *
 *    Unless required by applicable law or agreed to in writing, software
 *    distributed under the License is distributed on an "AS IS" BASIS,
 *    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *    See the License for the specific language governing permissions and
 *    limitations under the License.
 *
 * ************************************************************/
