using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public static class EntityEntryExtensions
{
    public static IEnumerable<string> FindPrimaryKeyNames<T>(this DbContext dbContext, T entity) {
        return from p in dbContext.FindPrimaryKeyProperties(entity) 
            select p.Name;
    }

    public static IEnumerable<object> FindPrimaryKeyValues<T>(this DbContext dbContext, T entity) {
        return from p in dbContext.FindPrimaryKeyProperties(entity) 
            select entity.GetPropertyValue(p.Name);
    }

    static IReadOnlyList<IProperty> FindPrimaryKeyProperties<T>(this DbContext dbContext, T entity) {
        var entityType = dbContext.Model.FindEntityType(typeof(T))
            ?? throw new InvalidOperationException($"Entity type '{typeof(T).Name}' is not part of the model.");
        var primaryKey = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException($"Entity type '{typeof(T).Name}' has no primary key.");
        return primaryKey.Properties;
    }

    static object GetPropertyValue<T>(this T entity, string name) {
        var property = entity!.GetType().GetProperty(name)
            ?? throw new InvalidOperationException($"Property '{name}' was not found on type '{typeof(T).Name}'.");
        // Primary-key values are non-null.
        return property.GetValue(entity)!;
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
