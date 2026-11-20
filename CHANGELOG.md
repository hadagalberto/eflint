# Changelog

All notable changes to this project are documented in this file.

## [Unreleased]

### Added

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
