// Portions Copyright .NET foundation
// Copyright 2025 Couchbase, Inc.
// This file is under an MIT license as granted under license from the .NET Foundation

using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using Microsoft.Extensions.Logging;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseRelationalDiagnosticsCommandLogger : RelationalCommandDiagnosticsLogger
{
    private DateTimeOffset _suppressCommandCreateExpiration;
    private DateTimeOffset _suppressCommandExecuteExpiration;
    private DateTimeOffset _suppressDataReaderClosingExpiration;
    private DateTimeOffset _suppressDataReaderDisposingExpiration;

    private readonly TimeSpan _loggingCacheTime;

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public CouchbaseRelationalDiagnosticsCommandLogger(ILoggerFactory loggerFactory, ILoggingOptions loggingOptions, DiagnosticSource diagnosticSource, LoggingDefinitions loggingDefinitions, IDbContextLogger contextLogger, IDbContextOptions contextOptions, IInterceptors? interceptors = null)
        : base(loggerFactory, loggingOptions, diagnosticSource, loggingDefinitions, contextLogger, contextOptions, interceptors)
    {
    }

    private bool ShouldLogParameterValues(DbCommand command)
        => command.Parameters.Count > 0 && ShouldLogSensitiveData();

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public override DbDataReader CommandReaderExecuted(
        IRelationalConnection connection,
        DbCommand command,
        DbContext? context,
        Guid commandId,
        Guid connectionId,
        DbDataReader methodResult,
        DateTimeOffset startTime,
        TimeSpan duration,
        CommandSource commandSource)
    {
        var definition = RelationalResources.LogExecutedCommand(this);

        if (ShouldLog(definition))
        {
            _suppressCommandExecuteExpiration = default;

            definition.Log(
                this,
                string.Format(CultureInfo.InvariantCulture, "{0:N0}", duration.TotalMilliseconds),
                command.Parameters.FormatParameters(ShouldLogParameterValues(command)),
                command.CommandType,
                command.CommandTimeout,
                Environment.NewLine,
                command.CommandText.TrimEnd());
        }

        return methodResult;
    }

    public void LogStatement(DbCommand command, TimeSpan duration)
    {
        var definition = RelationalResources.LogExecutedCommand(this);

        if (ShouldLog(definition))
        {
            _suppressCommandExecuteExpiration = default;

            definition.Log(
                this,
                string.Format(CultureInfo.InvariantCulture, "{0:N0}", duration.TotalMilliseconds),
                command.Parameters.FormatParameters(ShouldLogParameterValues(command)),
                command.CommandType,
                command.CommandTimeout,
                Environment.NewLine,
                command.CommandText.TrimEnd());
        }
    }
}
