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
The SQL++ generated looks like this:

```
SELECT `p0`.`PersonId`, `p0`.`Name`, `p0`.`PhotoId`, `p`.`PersonPhotoId`, `p`.`Caption`, `p`.`Photo`
FROM `Blogging`.`MyBlog`.`PersonPhoto` AS `p`
INNER JOIN `Blogging`.`MyBlog`.`Person` AS `p0` ON `p`.`PersonPhotoId` = `p0`.`PhotoId`
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

> [!NOTE] 
> Use FindAsync only when the entity might already be tracked by your context, and you want to avoid the database roundtrip. Otherwise, simply use SingleAsync - there is no performance difference between the two when the entity needs to be loaded from the database.


## Group By
EF Core also translates queries where an aggregate operator on the grouping appears in a Where or OrderBy (or other ordering) LINQ operator. It uses HAVING clause in SQL for the where clause. The part of the query before applying the GroupBy operator can be any complex query as long as it can be translated to server. Furthermore, once you apply aggregate operators on a grouping query to remove groupings from the resulting source, you can compose on top of it like any other query.
```
var query = from s in _context.Students
    group s by s.EnrollmentDate
    into grp
    select new EnrollmentDateGroup { EnrollmentDate = grp.Key, StudentCount = grp.Count() };
```
Which is translated into the following SQL++ statement:

```
SELECT `p`.`AuthorId` AS `Key`, COUNT(*) AS `Count`
FROM `Blogging`.`MyBlog`.`Posts` AS `p`
GROUP BY `p`.`AuthorId`
HAVING COUNT(*) > 0
ORDER BY `p`.`AuthorId`
```

### Supported Aggregate operators
The following aggregate operators are supported:
| .NET                         | SQL++             |
|------------------------------|-------------------|
| Average(x => x.Property)     | AVG(Property)     |
| Count()                      | COUNT(*)          |
| LongCount()                  | COUNT(*)          |
| Max(x => x.Property)         | MAX(Property)     |
| Min(x => x.Property)         | MIN(Property)     |
| Sum(x => x.Property)         | SUM(Property)     |

## Supported functions

Beyond the string methods available since 1.0 (`ToLower`/`ToUpper`, `Substring`, `Replace`,
`Trim`/`TrimStart`/`TrimEnd`, `Contains`), the provider translates the following .NET members to
SQL++ so they run server-side instead of throwing or falling back to client evaluation:

| .NET                                  | SQL++                              |
|----------------------------------------|-------------------------------------|
| `string.IndexOf(s)`                    | `POSITION(x, s)`                    |
| `string.StartsWith(s)`                 | `LIKE` (pattern-escaped)            |
| `string.EndsWith(s)`                   | `LIKE` (pattern-escaped)            |
| `string.IsNullOrEmpty(s)`               | `IS NULL OR = ''`                   |
| `string.PadLeft(n)` / `PadRight(n)`     | `LPAD(x, n)` / `RPAD(x, n)`          |
| `string.Length`                        | `LENGTH(x)`                         |
| `Math.Abs/Ceiling/Floor/Sqrt/Sign(x)`   | `ABS/CEIL/FLOOR/SQRT/SIGN(x)`       |
| `Math.Round(x[, d])`                   | `ROUND(x[, d])`                     |
| `Math.Truncate(x)`                     | `TRUNC(x)`                          |
| `Math.Pow(x, y)`                       | `POWER(x, y)`                       |
| `Math.Log(x)` / `Log10(x)` / `Exp(x)`  | `LN(x)` / `LOG(x)` / `EXP(x)`        |
| `Math.Log(x, newBase)`                 | `LN(x) / LN(newBase)`                |
| `DateTime.Year/Month/Day/Hour/Minute/Second/Millisecond/DayOfWeek/DayOfYear` | `DATE_PART_STR(x, part)` |
| `DateTime.Date`                        | `DATE_TRUNC_STR(x, 'day', fmt)`     |
| `DateTime.Now` / `.UtcNow` / `.Today`  | `NOW_LOCAL`/`NOW_UTC`/truncated `NOW_UTC` |
| `DateTime.AddYears/Months/Days/Hours/Minutes/Seconds(n)` | `DATE_ADD_STR(x, n, part)` |
| `Guid.NewGuid()`                       | `UUID()`                            |

`StartsWith`/`EndsWith` escape `%`/`_`/the escape character in the search value (constant patterns
are escaped once at translation time; parameter/column patterns are escaped at query time via
nested `REPLACE` calls) so a literal `%` or `_` in the search text is matched literally rather than
treated as a wildcard.

`DateTime` comparisons/arithmetic work against the ISO-8601 string format the provider stores
(millisecond precision, e.g. `2026-03-14T09:26:53.123Z`, with the fractional-seconds group and its
decimal point entirely omitted when milliseconds are exactly zero, e.g. `2026-03-14T00:00:00Z`).

Not yet supported: `Math.Min`/`Math.Max` (N1QL's `ARRAY_MAX`/`ARRAY_MIN` take a single array
argument, not two scalars — no array-literal expression support exists yet to build one), trig
functions (`Sin`/`Cos`/`Tan`/...), and secondary-index support for EF Core's `HasIndex()` (see
[Limitations](limitations.md)).

## SQL queries

> [!NOTE]
> Synchronous enumeration (`.ToList()`, `.First()`, etc.) of a `FromSqlRaw`/`FromSql` query that
> hasn't been composed with further LINQ operators (`Where`, `OrderBy`, ...) throws
> `NotImplementedException` — this applies to both `FromSqlRaw` and the interpolated `FromSql`
> overload alike, not just one of them. The async form (`ToListAsync`/`FirstOrDefaultAsync`/etc.)
> works for both and is used throughout the examples below.

### FromSqlRaw
`FromSqlRaw` lets you write a raw SQL++ query directly, with parameters passed positionally (`{0}`, `{1}`, ...) rather than interpolated into the string — this is the recommended way to include variable data in a raw query, since it goes through the database parameter mechanism instead of string concatenation:
```
string query = "SELECT p.* FROM `Blogging`.`MyBlog`.`Person` as p WHERE PersonId={0}";
var person = await context.Set<Person>()
    .FromSqlRaw(query, 1)
    .FirstOrDefaultAsync();
```

Couchbase's `META().id` (the document key) can be queried the same way — useful when you want to fetch by key through a raw query rather than `FindAsync`:
```
var blog = await context.Blogs
    .FromSqlRaw("SELECT `b`.* FROM `default`.`blogs`.`blog` AS `b` WHERE META(`b`).id = \"1\"")
    .FirstOrDefaultAsync();
```

`FromSqlRaw` supports multiple parameters and composes with `OrderBy`/paging like any other query source:
```
const string sql = @"SELECT DISTINCT route.destinationairport
    FROM `travel-sample`.`inventory`.`airport` AS airport
    JOIN `travel-sample`.`inventory`.`route` AS route
      ON route.sourceairport = airport.faa
    WHERE LOWER(airport.faa) = {0}
      AND route.stops = 0
    ORDER BY route.destinationairport
    LIMIT {1}
    OFFSET {2}";

var destinations = await context.Set<DestinationAirport>()
    .FromSqlRaw(sql, airportCode, pageSize, skip)
    .ToListAsync();
```

### FromSql

`FromSql` (the interpolated-string overload, `context.Blogs.FromSql($"...")`) works the same way as `FromSqlRaw` above, including the async-only caveat noted at the top of this section:
```
var blogs = await context.Blogs
    .FromSql($"SELECT `b`.* FROM `default`.`blogs`.`blog` AS `b` WHERE META(`b`).id = \"1\"")
    .ToListAsync();
```
