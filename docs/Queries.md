# Querying with the EF Core Couchbase DB Provider

## Querying basics
[EF Core LINQ queries](https://learn.microsoft.com/en-us/ef/core/querying/) can be executed against EF Core Couchbase DB in the same way as for other database providers. For example:

```
public class Session
{
    public Guid Id { get; set; }
    public string Category { get; set; }

    public string TenantId { get; set; } = null!;
    public Guid UserId { get; set; }
    public int SessionId { get; set; }
}

var stringResults = await context.Sessions
    .Where(
        e => e.Category.Length > 4
            && e.Category.Trim().ToLower() != "disabled"
            && e.Category.TrimStart().Substring(2, 2).Equals("xy", StringComparison.OrdinalIgnoreCase))
    .ToListAsync();
```

## Joins
The LINQ Join operator allows you to connect two data sources based on the key selector for each source, generating a tuple of values when the key matches. It naturally translates to INNER JOIN on relational databases. While the LINQ Join has outer and inner key selectors, the database requires a single join condition. So EF Core generates a join condition by comparing the outer key selector to the inner key selector for equality.
```
var query = from photo in context.Set<PersonPhoto>()
    join person in context.Set<Person>()
        on photo.PersonPhotoId equals person.PhotoId
    select new { person, photo };
```


## FirstAsync

```
var session = await context.Sessions.FirstAsync(x => x.SessionId == 2);
```

## Pagination
Pagination refers to retrieving results in pages, rather than all at once; this is typically done for large resultsets, where a user interface is displayed, allowing users to navigate through pages of the results.

A common way to implement pagination with databases is to use the Skip and Take LINQ operators (OFFSET and LIMIT in SQL++). Given a page size of 10 results, the third page can be fetched with EF Core as follows:

```
var position = 20;
var nextPage = context.Sessions
    .OrderBy(s => s.Id)
    .Skip(position)
    .Take(10)
    .ToList();
```

## Aggregation
Aggregation functions such as SUM can be combined with GROUPBY:
```
var query = from s in _context.Students
    group s by s.EnrollmentDate
    into grp
    select new EnrollmentDateGroup { EnrollmentDate = grp.Key, StudentCount = grp.Count() };
```

## First Async
[FindAsync ](https://learn.microsoft.com/en-us/ef/core/change-tracking/entity-entries#find-and-findasync)is a useful API for getting an entity by its primary key, and avoiding a database roundtrip when the entity has already been loaded and is tracked by the context:

```
public class Session
{
    public Guid Id { get; set; }
    ...
}

var mySession = await context.FindAsync(pkey);
```

> [NOTE] Use FindAsync only when the entity might already be tracked by your context, and you want to avoid the database roundtrip. Otherwise, simply use SingleAsync - there is no performance difference between the two when the entity needs to be loaded from the database.


## Group By

```
var query = from s in _context.Students
    group s by s.EnrollmentDate
    into grp
    select new EnrollmentDateGroup { EnrollmentDate = grp.Key, StudentCount = grp.Count() };
```

### Supported Aggregate operators
| .NET                         | SQL               |
|------------------------------|-------------------|
| ~~Average(x => x.Property)~~ | ~~AVG(Property)~~ |
| Count()                      | COUNT(*)          |
| LongCount()                  | COUNT(*)          |
| Max(x => x.Property)         | MAX(Property)     |
| Min(x => x.Property)         | MIN(Property)     |
| Sum(x => x.Property)         | SUM(Property)     |

[^1]: Average is not supported in the Developer Preview. See https://jira.issues.couchbase.com/browse/NCBC-3891

## SQL queries


> [NOTE] DbContext.FromSql and DbContext.FromSqlRaw will through a NotImplementedException in EF Core Couchbase DB Provider Developer Preview 1.

