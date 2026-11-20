# EfLint

Roslyn analyzers and code fixes for Entity Framework Core. EfLint catches EF Core performance, correctness and security pitfalls at compile time.

## Install

```
dotnet add package EfLint
```

## Rules

| ID | Category | Summary |
| --- | --- | --- |
| EFL1001 | Performance | Query materialized before filtering or projection. |
| EFL1002 | Performance | `Count()` compared with zero/one instead of `Any()`. |
| EFL1003 | Performance | Read-only query without `AsNoTracking()`. |
| EFL1004 | Performance | Possible N+1 query pattern in a loop. |
| EFL1005 | Performance | `First`/`FirstOrDefault` without `OrderBy`. |
| EFL1006 | Performance | `Skip`/`Take` without `OrderBy`. |
| EFL1007 | Performance | `SaveChanges` inside a loop. |
| EFL1008 | Performance | Entity loaded only to be deleted or updated. |
| EFL1009 | Performance | Large in-memory collection used with `Contains` in a query. |
| EFL1010 | Performance | Multiple collection `Include`s without `AsSplitQuery()`. |
| EFL2001 | Correctness | Synchronous EF operation inside an `async` method. |
| EFL2002 | Correctness | Async EF operation ignores an available `CancellationToken`. |
| EFL2003 | Correctness | Non-translatable user method inside an EF query lambda. |
| EFL2004 | Correctness | `DbContext` captured by `Parallel`/`Task.WhenAll` lambdas. |
| EFL2005 | Correctness | `DbContext` registered as a singleton. |
| EFL2006 | Correctness | `Include` ignored after a `Select` projection. |
| EFL2007 | Correctness | Non-sargable string comparison inside an EF query. |
| EFL2008 | Correctness | Redundant `Update`/`Attach` on an already tracked entity. |
| EFL3001 | Security | Raw SQL built from interpolated or concatenated non-constant values (SQL injection). |
| EFL3002 | Security | Interpolated SQL flattened into a `string` before being passed to a raw SQL API. |

All rules are configurable through `.editorconfig`, for example:

```ini
dotnet_diagnostic.EFL3001.severity = error
eflint.provider = postgres
```
