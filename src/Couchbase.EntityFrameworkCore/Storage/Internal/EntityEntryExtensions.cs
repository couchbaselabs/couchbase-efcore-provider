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
        return dbContext.Model.FindEntityType(typeof(T)).FindPrimaryKey().Properties;
    }

    static object GetPropertyValue<T>(this T entity, string name) {
        return entity.GetType().GetProperty(name).GetValue(entity, null);
    }

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
