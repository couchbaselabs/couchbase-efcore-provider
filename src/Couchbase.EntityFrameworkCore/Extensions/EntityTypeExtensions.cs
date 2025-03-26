using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Couchbase.EntityFrameworkCore.Extensions;

public static class EntityTypeExtensions
{
    public static string GetCollectionName(this IEntityType entityType)
    {
        return entityType.GetTableName();
    }

    public static string GetPrimaryKey(this IEntityType entityType, object entity)
    {
        var keys = entityType.FindPrimaryKey().Properties
            .ToArray(); //TODO If no primary key found we should fail hard

        var compositeKey = new StringBuilder();
        foreach (var property in entity.GetType().GetProperties().Reverse())
        {
            foreach (var key in keys)
            {
                if (key.Name != property.Name) continue;
                if (compositeKey.Length > 0)
                {
                    compositeKey.Append("_"); //TODO delimiter should be optional and customizable
                }

                var keyValue = property.GetValue(entity);
                compositeKey.Append(keyValue);
            }
        }

        return compositeKey.ToString();
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
