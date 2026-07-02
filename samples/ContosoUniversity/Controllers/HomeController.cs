using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using ContosoUniversity.Models;
using Microsoft.EntityFrameworkCore;
using ContosoUniversity.Data;
using ContosoUniversity.Models.SchoolViewModels;
using Couchbase.EntityFrameworkCore.Metadata;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace ContosoUniversity.Controllers
{
    public class HomeController : Controller
    {
        private readonly SchoolContext _context;

        public HomeController(SchoolContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            return View();
        }

        public async Task<ActionResult> About()
        {
            List<EnrollmentDateGroup> groups = new List<EnrollmentDateGroup>();
            var conn = _context.Database.GetDbConnection();

            try
            {
                await conn.OpenAsync();
                await using var command = conn.CreateCommand();
                // Resolve the fully-qualified keyspace (Bucket.Scope.Collection) from the model
                // rather than hardcoding it, so the query follows the entity's mapping if the
                // bucket/scope change. Student is part of the Person TPH hierarchy, so its table
                // name is the shared "person" keyspace.
                var keyspace = CouchbaseKeyspace
                    .Parse(_context.Model.FindEntityType(typeof(Student))!.GetTableName()!)
                    .ToSqlString();
                // Field names are camelCase because the context uses UseCamelCaseNamingConvention();
                // the hierarchy is distinguished by the "discriminator" field.
                string query = "SELECT enrollmentDate AS EnrollmentDate, COUNT(*) AS StudentCount "
                               + $"FROM {keyspace} "
                               + "WHERE discriminator = 'Student' "
                               + "GROUP BY enrollmentDate";
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
            finally
            {
                conn.Close();
            }
            return View(groups);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
