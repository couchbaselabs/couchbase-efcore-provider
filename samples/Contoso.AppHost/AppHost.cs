using Couchbase.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Spin up a Couchbase Server container and provision the keyspace the ContosoUniversity
// app maps its entities to (see SchoolContext): bucket "Universities", scope "Contoso",
// and one collection per entity type.
var couchbase = builder.AddCouchbase("couchbase");

var universities = couchbase.AddBucket("Universities");
universities.WithScope("Contoso",
[
    "course",
    "enrollment",
    "person",
    "department",
    "officeAssignment",
    "courseAssignment"
]);

// Inject the bucket's connection string into the web app (as ConnectionStrings:Universities)
// and hold the app until the bucket, scope, and collections are ready.
builder.AddProject<Projects.ContosoUniversity>("contosouniversity")
    .WithReference(universities)
    .WaitFor(universities);

builder.Build().Run();
