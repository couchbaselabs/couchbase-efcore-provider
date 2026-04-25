using System.Runtime.CompilerServices;

#if DEBUG
[assembly: InternalsVisibleTo("Couchbase.EntityFrameworkCore.FunctionalTests")]
#endif

[assembly: InternalsVisibleTo("Couchbase.EntityFrameworkCore.UnitTests")]
[assembly: InternalsVisibleTo("Couchbase.EntityFrameworkCode.IntegrationTests")]