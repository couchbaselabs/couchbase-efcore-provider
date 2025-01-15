using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ç;

public class CouchbaseLoggingDefinitions : RelationalLoggingDefinitions
{
    public EventDefinitionBase? LogExecutingSqlQuery;

    public EventDefinitionBase? LogExecutingReadItem;

    public EventDefinitionBase? LogExecutedReadNext;

    public EventDefinitionBase? LogExecutedReadItem;

    public EventDefinitionBase? LogExecutedCreateItem;

    public EventDefinitionBase? LogExecutedReplaceItem;

    public EventDefinitionBase? LogExecutedDeleteItem;
}