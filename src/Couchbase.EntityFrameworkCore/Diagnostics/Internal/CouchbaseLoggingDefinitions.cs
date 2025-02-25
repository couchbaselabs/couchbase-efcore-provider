using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Couchbase.EntityFrameworkCore.Diagnostics.Internal;

public class CouchbaseLoggingDefinitions : RelationalLoggingDefinitions
{
    public EventDefinitionBase? LogSchemaConfigured;

    public EventDefinitionBase? LogSequenceConfigured;

    public EventDefinitionBase? LogUsingSchemaSelectionsWarning;

    public EventDefinitionBase? LogFoundColumn;

    public EventDefinitionBase? LogFoundForeignKey;

    public EventDefinitionBase? LogForeignKeyScaffoldErrorPrincipalTableNotFound;

    public EventDefinitionBase? LogFoundTable;

    public EventDefinitionBase? LogMissingTable;

    public EventDefinitionBase? LogPrincipalColumnNotFound;

    public EventDefinitionBase? LogFoundIndex;

    public EventDefinitionBase? LogFoundPrimaryKey;

    public EventDefinitionBase? LogFoundUniqueConstraint;

    public EventDefinitionBase? LogUnexpectedConnectionType;

    public EventDefinitionBase? LogTableRebuildPendingWarning;

    public EventDefinitionBase? LogCompositeKeyWithValueGeneration;

    public EventDefinitionBase? LogInferringTypes;

    public EventDefinitionBase? LogOutOfRangeWarning;

    public EventDefinitionBase? LogFormatWarning;
}