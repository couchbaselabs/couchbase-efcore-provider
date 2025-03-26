using System.Data.Common;
using Couchbase.EntityFrameworkCore.Infrastructure;
using Couchbase.EntityFrameworkCore.Infrastructure.Internal;
using Couchbase.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Couchbase.EntityFrameworkCore.Storage.Internal;

public class CouchbaseRelationalConnection : RelationalConnection, ICouchbaseConnection
{
    private IDbContextTransaction? _currentTransaction;
    private IDbContextTransaction? _currentTransaction1;
    private IRelationalCommand _relationalCommand;
    private readonly IRelationalCommandBuilder _relationalCommandBuilder;
    private readonly IBucketProvider _bucketProvider;
    private readonly ICouchbaseDbContextOptionsBuilder _couchbaseDbContextOptionsBuilder;
    private readonly RelationalConnectionDependencies _dependencies;
    private IDiagnosticsLogger<DbLoggerCategory.Infrastructure> _logger;
    private string _connectionString;
    private ClusterOptions _clusterOptions;
    
    /*
     *       var conn = _context.Database.GetDbConnection();
             try
             {
                 await conn.OpenAsync();
                 using (var command = conn.CreateCommand())
                 {
                     string query = "SELECT EnrollmentDate, COUNT(*) AS StudentCount "
                         + "FROM Person "
                         + "WHERE Discriminator = 'Student' "
                         + "GROUP BY EnrollmentDate";
                     command.CommandText = query;
                     DbDataReader reader = await command.ExecuteReaderAsync();

                     if (reader.HasRows)
                     {
                         while (await reader.ReadAsync())
                         {
                             var row = new EnrollmentDateGroup { EnrollmentDate = reader.GetDateTime(0), StudentCount = reader.GetInt32(1) };
                             groups.Add(row);
                         }
                     }
                     reader.Dispose();
                 }
             }
             finally
             {
                 conn.Close();
             }
     */

    public CouchbaseRelationalConnection(RelationalConnectionDependencies dependencies,
        IDiagnosticsLogger<DbLoggerCategory.Infrastructure> logger,
        IRelationalCommandBuilder relationalCommandBuilder,
        IBucketProvider bucketProvider,
        ICouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder) : base(dependencies)
    {
        _dependencies = dependencies;
        _logger = logger;
        _relationalCommandBuilder = relationalCommandBuilder;
        _bucketProvider = bucketProvider;
        _couchbaseDbContextOptionsBuilder = couchbaseDbContextOptionsBuilder;

        var optionsExtension = dependencies.ContextOptions.Extensions.OfType<CouchbaseOptionsExtension>().FirstOrDefault();
        if (optionsExtension != null)
        {
            _connectionString = optionsExtension.ConnectionString;
            _clusterOptions = optionsExtension.DbContextOptionsBuilder.ClusterOptions;

            var relationalOptions = RelationalOptionsExtension.Extract(dependencies.ContextOptions);
            if (relationalOptions.Connection != null)
            {
              //  InitializeDbConnection(relationalOptions.Connection);
            }
        }
    }

    protected override DbConnection CreateDbConnection()
    {
        return new CouchbaseConnection(_bucketProvider, _couchbaseDbContextOptionsBuilder);
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
