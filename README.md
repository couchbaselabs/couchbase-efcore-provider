# EFCore.Couchbase

## Getting started:
* Requires [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) to build
* The sample app, Contoso University, requires a Couchbase instance installed on localhost:8091
* You need to create a bucket called "contoso" on the Couchbase instance
* The following scopes and collections must be created:
   * courseAssignment.courseAssignment
   * department.department
   * enrollment.enrollment
   * officeAssignment.officeAssignment
   * person.person
   * course.course
 * The following indexes need to be created (if CB 7.6.0 or later you may not need to create the indexes):
   * CREATE INDEX `idxcourses` ON `contoso`.`course`.`course`(self,`courseid`)
   * CREATE PRIMARY INDEX `#primary` ON `contoso`.`course`.`course`
   * CREATE PRIMARY INDEX `#primary` ON `contoso`.`courseAssignment`.`courseAssignment`
   * CREATE PRIMARY INDEX `#primary` ON `contoso`.`department`.`department`
   * CREATE INDEX `idxFoo` ON `contoso`.`department`.`department`(`DepartmentID`)
   * CREATE PRIMARY INDEX `#primary` ON `contoso`.`enrollment`.`enrollment`
   * CREATE INDEX `idxOfficeAssignment` ON `contoso`.`officeAssignment`.`officeAssignment`(`InstructorID`)
   * CREATE PRIMARY INDEX `#primary` ON `contoso`.`person`.`person`
   * Any others that I missed ;)
 * Assuming you have the bucket, scopes/collections, and the indexes setup, it should just work...except for the Instructers page (NullReferenceException) because it uses Include/ThenInclude which currently isn't supported.
 * If you want to step through EFCore or EFCore.Relational projects your will need to pull the EFCore repo (git@github.com:dotnet/efcore.git) into an adjacent directory and then add the Couchbase.EFCore and ContosoUniversity projects to the EFCore solution.

 # What works:
 * Basic projections/queries
 * Some SQL++ functions - COUNT, CONTAINS, etc
 * Basic CRUD and change tracking

 # What doesn't work
 * Include/ThenInclude (Instucters page in the Contoso app)
 * Most all SQL++ functions
 * Value generation

   
