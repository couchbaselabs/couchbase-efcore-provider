using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Couchbase.EntityFrameworkCore.Internal;
using Microsoft.Extensions.Logging;

namespace Couchbase.EntityFrameworkCore.Diagnostics.Internal;

/// <summary>Logging for Couchbase-specific events.</summary>
public static class CouchbaseLoggerExtensions
{
    internal const string SyncNotSupportedMessage =
        "The Couchbase EF Core provider performs I/O asynchronously only, and synchronous calls (SaveChanges, "
        + "synchronous queries such as ToList or First, BeginTransaction, Commit, EnsureCreated) are disabled by default. "
        + "Use and await the async methods, or opt in to blocking synchronous calls with "
        + "ConfigureWarnings(w => w.Ignore(CouchbaseEventId.SyncNotSupported)).";

    /// <summary>
    /// Logs <see cref="CouchbaseEventId.SyncNotSupported"/>. Configured to throw by default (see
    /// <c>UseCouchbase</c>); when the application ignores or logs it, the caller then runs the
    /// asynchronous operation and blocks on it.
    /// </summary>
    public static void SyncNotSupported<TLoggerCategory>(this IDiagnosticsLogger<TLoggerCategory> diagnostics)
        where TLoggerCategory : LoggerCategory<TLoggerCategory>, new()
    {
        var definition = LogSyncNotSupported(diagnostics);

        if (diagnostics.ShouldLog(definition))
        {
            definition.Log(diagnostics);
        }

        if (diagnostics.NeedsEventData(definition, out var diagnosticSourceEnabled, out var simpleLogEnabled))
        {
            var eventData = new EventData(definition, static (d, _) => ((EventDefinition)d).GenerateMessage());
            diagnostics.DispatchEventData(definition, eventData, diagnosticSourceEnabled, simpleLogEnabled);
        }
    }

    private static EventDefinition LogSyncNotSupported(IDiagnosticsLogger logger)
    {
        var definitions = (CouchbaseLoggingDefinitions)logger.Definitions;
        var definition = definitions.LogSyncNotSupported;
        if (definition == null)
        {
            definition = NonCapturingLazyInitializer.EnsureInitialized(
                ref definitions.LogSyncNotSupported,
                logger,
                static logger => new EventDefinition(
                    logger.Options,
                    CouchbaseEventId.SyncNotSupported,
                    LogLevel.Warning,
                    "CouchbaseEventId.SyncNotSupported",
                    level => LoggerMessage.Define(level, CouchbaseEventId.SyncNotSupported, SyncNotSupportedMessage)));
        }

        return (EventDefinition)definition;
    }
}
