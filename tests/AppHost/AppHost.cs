using Couchbase.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var couchbase = builder.AddCouchbase("couchbase");
var defaultBucket = couchbase.AddBucket("default");
defaultBucket.WithScope("blogs",
    ["blog", "post", "person", "tag", "posttag", "personphoto"]);
defaultBucket.WithScope("contoso",
[
    "course", "enrollment", "person", "department", "officeAssignment",
    "courseAssignment"
]);
defaultBucket.WithScope("ownedtypes", ["customer"]);
// Shared scope/collection used by the multi-bucket isolation test — the same
// scope and collection name also exists in the secondary bucket below, so the
// test can prove context-per-bucket isolation with an identical keyspace shape.
defaultBucket.WithScope("isolation", ["widget"]);

// Second bucket on the same cluster for the multi-bucket DI isolation test.
var secondaryBucket = couchbase.AddBucket("secondary");
secondaryBucket.WithScope("isolation", ["widget"]);

var sampleBucket = couchbase.AddSampleBucket("travel-sample", "travel-sample");
builder.Build().Run();
