# Changelog

All notable changes to this project are documented in this file.

## [Unreleased]

### Added

- Solution scaffold: analyzers, code fixes, NuGet package, tests, sample, CI and docs.
- Shared infrastructure: EF Core symbol cache via `RegisterCompilationStartAction`,
  `IOperation` query-chain helpers and `.editorconfig` option reader.
- EFL3001 — raw SQL built from dynamic values, with a FormattableString code fix.
- EFL3002 — interpolated SQL flattened into a `string` local.
- Analyzer tests with real EF Core reference assemblies, code-fix and Fix All tests,
  sample integration test, package layout test and a 2,000-method performance test.
