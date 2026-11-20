# EfLint — SPEC

**EfLint**: Roslyn analyzers + code fixes that catch Entity Framework Core performance, correctness and security pitfalls at compile time.
Ships as one NuGet analyzer package (`EfLint`), works in Visual Studio, Rider, VS Code and `dotnet build`. Open source, MIT, "Hadagalberto Junior".
NOT a toy: every rule has precise semantics (symbol-based, not string matching), documentation, configurable severity, and exhaustive tests with the
official Roslyn testing framework. No stubs, no TODO, no placeholder or skipped tests.

## Layout

```
EfLint.sln
src/EfLint/                 analyzers (netstandard2.0, Microsoft.CodeAnalysis.CSharp, matching the Roslyn version supported by the .NET 8 SDK)
src/EfLint.CodeFixes/       code fix providers (netstandard2.0)
src/EfLint.Package/         packs analyzers + fixes into analyzers/dotnet/cs, includes build/EfLint.props, README
tests/EfLint.Tests/         Microsoft.CodeAnalysis.CSharp.Analyzer.Testing (xUnit) with real EF Core reference assemblies (ReferenceAssemblies + EF Core package)
samples/EfLint.Sample/      a small app that triggers every rule (excluded from warnaserror) used in an integration test that asserts the exact diagnostics list
docs/rules/EFLxxxx.md       one page per rule: what, why (with links to official EF Core docs), bad/good examples, configuration
.github/workflows/ci.yml, Directory.Build.props (nullable, warnaserror, deterministic), README.md + README.pt-BR.md, CHANGELOG.md, CONTRIBUTING.md, LICENSE, AnalyzerReleases.Shipped/Unshipped.md
```

## Rules (IDs EFL1xxx performance, EFL2xxx correctness, EFL3xxx security). Each: analyzer + code fix where marked (fix).

Security
- EFL3001 `FromSqlRaw`/`ExecuteSqlRaw`/`SqlQueryRaw` called with an interpolated string or string concatenation containing non-constant values → SQL injection. Fix: convert to `FromSql`/`ExecuteSql`/`SqlQuery` (FormattableString) (fix).
- EFL3002 `FromSqlInterpolated` misuse: interpolated string assigned to `string` variable first then passed to Raw (data-flow within method).

Correctness
- EFL2001 Sync-over-async: calling `ToList()/First()/Count()/Any()/SaveChanges()/Find()` etc. on `IQueryable` from EF inside an `async` method when an `...Async` overload exists (fix: use async overload + await, pass CancellationToken if available in scope).
- EFL2002 `async` query without passing an in-scope `CancellationToken` (fix).
- EFL2003 Client evaluation trap: calling a non-translatable user method inside `Where/OrderBy/Select` lambdas on an EF `IQueryable` (method not marked `[DbFunction]` and not a known translatable BCL member) — maintain a curated allowlist of translatable members (string, Math, DateTime, EF.Functions...).
- EFL2004 `DbContext` captured/used across threads: `Parallel.ForEach`/`Task.WhenAll` lambdas using the same context instance.
- EFL2005 `DbContext` registered as Singleton (`AddSingleton<MyContext>` or `AddDbContext(..., ServiceLifetime.Singleton)`).
- EFL2006 `Include` on a query projected with `Select` (Include is ignored) (fix: remove Include).
- EFL2007 Comparing strings with `string.Equals(a, b, StringComparison.X)` / `ToLower()==` inside queries (non-sargable / not translated as expected) — suggest `EF.Functions.Collate` or `ILike` depending on provider (info).
- EFL2008 `Update()`/`Attach()` on an entity obtained from the same tracked context (redundant, causes full update).

Performance
- EFL1001 `ToList()/ToArray()/AsEnumerable()` before `Where/Select/OrderBy/Skip/Take/Count/Any` → query materialised early (fix: move the materialisation to the end).
- EFL1002 `Count() > 0` / `Count() != 0` / `Count() >= 1` → `Any()` (fix).
- EFL1003 Read-only query without `AsNoTracking()` when results are only read and never modified in the method and no `SaveChanges` follows (conservative flow analysis; info by default) (fix).
- EFL1004 N+1: navigation property access inside a `foreach` over query results where the navigation was not `Include`d and lazy loading proxies are enabled or the loop issues queries per item (`context.X.Where(... item ...)` inside loop) (fix for the second form: none; message suggests Include/batched query).
- EFL1005 `First()/FirstOrDefault()` without `OrderBy` on a query (non-deterministic) (warning) .
- EFL1006 `Skip/Take` pagination without `OrderBy`.
- EFL1007 `SaveChanges` called inside a loop (fix: move after the loop when the loop body has no dependency on generated keys — otherwise message only).
- EFL1008 Loading a whole entity to delete/update by id → suggest `ExecuteDelete`/`ExecuteUpdate` (EF Core 7+) (info).
- EFL1009 `Contains` on a large in-memory collection inside query — informational with guidance.
- EFL1010 Missing `AsSplitQuery()` with multiple collection `Include`s (cartesian explosion) (fix).

Detection must be symbol-based: types resolved via `Compilation.GetTypeByMetadataName` for `Microsoft.EntityFrameworkCore.DbContext`, `DbSet<T>`, `IQueryable<T>`,
`EntityFrameworkQueryableExtensions`, `RelationalQueryableExtensions`, `RelationalDatabaseFacadeExtensions`. If EF Core is not referenced, analyzers do nothing (zero cost).
Use `RegisterCompilationStartAction`, `IOperation` analysis where possible, `EnableConcurrentExecution`, `ConfigureGeneratedCodeAnalysis(None)`.
All diagnostics configurable via `.editorconfig` (`dotnet_diagnostic.EFL1001.severity`) and options (`eflint.EFL1003.enabled`, provider hint `eflint.provider = postgres|sqlserver|sqlite`).

## Quality bar

- For every rule: ≥ 6 positive tests and ≥ 6 negative tests (false-positive guards), and every code fix has before/after tests including Fix All.
- Integration test building `samples/EfLint.Sample` asserting the exact diagnostics set.
- Analyzer performance test: analyze a generated 2,000-method project under a time budget; no allocations-heavy patterns (no `SyntaxNode.ToString()` matching).
- RS1xxx analyzer-authoring rules (Microsoft.CodeAnalysis.Analyzers) all clean. `dotnet build -warnaserror` clean, `dotnet test` green, `dotnet pack` produces a valid analyzer package (test consumes the packed nupkg in a temp project).
- README: rules table with links, install, configuration, screenshots not required.

## Hard constraints
- Never run `git push`, `gh`, `dotnet nuget push`, or publish anything. Commit locally after each milestone. Stay inside this repository.
- Verify EF Core API names against the actual referenced EF Core assemblies (inspect them), never guess method names.
