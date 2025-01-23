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
    private readonly IServiceProvider _serviceProvider;
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
        IServiceProvider serviceProvider,
        ICouchbaseDbContextOptionsBuilder couchbaseDbContextOptionsBuilder) : base(dependencies)
    {
        _dependencies = dependencies;
        _logger = logger;
        _relationalCommandBuilder = relationalCommandBuilder;
        _serviceProvider = serviceProvider;
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
        return new CouchbaseConnection(_serviceProvider, _couchbaseDbContextOptionsBuilder);
    }
}