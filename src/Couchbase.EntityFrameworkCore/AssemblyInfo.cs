using System.Runtime.CompilerServices;

#if DEBUG
[assembly: InternalsVisibleTo("Couchbase.EntityFrameworkCore.FunctionalTests")]
[assembly: InternalsVisibleTo("Couchbase.EntityFrameworkCore.UnitTests")]
[assembly: InternalsVisibleTo("Couchbase.EntityFrameworkCode.IntegrationTests")]
#endif