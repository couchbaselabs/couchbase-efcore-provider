# Contoso University Sample App:

This the sample app that is included in the [Tutorial: Get started with EF Core in an ASP.NET MVC wep app](https://learn.microsoft.com/en-us/aspnet/core/data/ef-mvc/intro?view=aspnetcore-8.0) that uses Couchbase EFCore instead of Sqlite EFCore. 

## Prerequisites
* Requires [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) to build
* Setup Couchbase Server:
  * [Couchbase Capella ](https://www.couchbase.com/downloads/?family=capella) Cloud has a [free-tier](https://docs.couchbase.com/cloud/get-started/create-account.html#sign-up-free-tier) that makes it easy to get up and running. 
  * Refer to the [documentation](https://docs.couchbase.com/home/cloud.html) for getting up and running with Capella

## Create the Couchbase Server Bucket

Couchbase Server uses Buckets in place of RDBMS database instances. For the Contoso University app, we will create a Bucket named "Universities" with a Scope named "Contoso" and a Collection named "Course":

![img_14.png](img_14.png)

Finally, we will create Collections for each entity in the Contoso model under the "Contoso" Scope (Course, CourseAssignment, Department, Enrollment, OfficeAssignment and Person):

![img_15.png](img_15.png)

> [!NOTE]
> All names are **case-sensitive** from the Bucket to the Scope and Collection names.

When the application loads, it will generate the documents for the model and store them in the Bucket.

## Running the Contoso University web app
Depending upon which IDE or editor you are using, you will either:
* _Run 'CouchbaseGettingStarted'_ in Rider IDE Or,
* _Debug > Start Without Debugging_ in VS Or,
* `dotnet run` in .NET CLI

![img_9.png](img_9.png)

