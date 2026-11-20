# Changelog

All notable changes to this project are documented in this file.

## [Unreleased]

### Fixed

- Maintainer fix pass: query rules now fire in a real consumer built from the packed
  nupkg. The query-chain tracer follows `IEnumerable`/`Enumerable` extensions invoked
  on `DbSet<T>` (which binds to `Enumerable.ToList`/`Count`, not `Queryable`), DbSet
  auto-properties, expression-bodied properties and fields, top-level statements,
  `IQueryable<T>` locals and source-declared factories returning `IQueryable<T>`
  (including parameter substitution so the originating `DbContext` is still identified).
- Packed-package tests consume the freshly built nupkg instead of a stale global NuGet
  cache entry, and compile with the packed analyzer DLLs passed directly to csc.

### Added

- Integration test that compiles the exact maintainer reproduction against the packed
  nupkg and asserts its exact diagnostic set, plus top-level-statement and DbSet-shape
  variants and receiver-shape coverage for all query-derived rules.
- Solution scaffold: analyzers, code fixes, NuGet package, tests, sample, CI and docs.
- Shared infrastructure: EF Core symbol cache via `RegisterCompilationStartAction`,
  `IOperation` query-chain helpers and `.editorconfig` option reader.
- Performance rules EFL1001–EFL1010 with code fixes for EFL1001, EFL1002, EFL1003,
  EFL1007 and EFL1010, each covered by positive, negative and Fix All tests.
- Code fixes now preserve the document's end-of-line style; all code-fix tests run against
  both LF and CRLF sources and `.gitattributes` normalizes source line endings.
- EFL3001 — raw SQL built from dynamic values, with a FormattableString code fix.
- EFL3002 — interpolated SQL flattened into a `string` local.
- EFL2001 — synchronous EF operation in an async method, with an async/await code fix.
- EFL2002 — async EF operation without an in-scope CancellationToken, with a code fix.
- EFL2003 — non-translatable user method inside an EF query lambda.
- EFL2004 — DbContext captured by Parallel/Task.WhenAll lambdas.
- EFL2005 — DbContext registered with a singleton lifetime.
- EFL2006 — Include discarded by a Select projection, with a removal code fix.
- EFL2007 — non-sargable string comparisons inside EF queries.
- EFL2008 — redundant Update/Attach on an entity tracked by the same context.
- Analyzer tests with real EF Core reference assemblies, code-fix and Fix All tests,
  sample integration test, package layout test and a 2,000-method performance test.
- Sample project now triggers all 20 rules; the integration test asserts the exact
  21-diagnostic set (all rules normalized to `warning` in the sample's `.editorconfig`).
  The sample is excluded from the solution build configuration so `dotnet build -warnaserror`
  stays clean while the integration test still builds it and sees its diagnostics.
- Maintainer fix pass: sample demotes every descriptor id via `WarningsNotAsErrors` (guard
  test generates the list from `EfLintDiagnostics`), integration test covers `-warnaserror`,
  CI runs `dotnet build -warnaserror` at the root, and code-fix line-ending coverage is
  verified by an automated guard.
