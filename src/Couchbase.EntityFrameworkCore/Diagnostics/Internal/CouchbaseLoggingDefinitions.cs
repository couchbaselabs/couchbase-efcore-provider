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

/* ************************************************************
 *
 *    @author Couchbase <info@couchbase.com>
 *    @copyright 2021 Couchbase, Inc.
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
