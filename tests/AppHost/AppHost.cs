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

var sampleBucket = couchbase.AddSampleBucket("travel-sample", "travel-sample");
builder.Build().Run();
