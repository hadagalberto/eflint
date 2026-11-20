# Contributing

## Prerequisites

- .NET 8 SDK (see `global.json`)
- Git

## Workflow

1. Create a branch from `main`.
2. Implement the rule and its code fix (when applicable).
3. Add at least six positive and six negative tests using
   `Microsoft.CodeAnalysis.Testing` with the real EF Core reference assemblies, plus
   before/after tests for every code fix (including Fix All).
4. Update `docs/rules/EFLxxxx.md`, `AnalyzerReleases.Unshipped.md` and the README table.
5. Verify EF Core API names against the referenced assemblies before using them.
6. Run:

   ```shell
   dotnet build EfLint.sln
   dotnet test EfLint.sln
   ```

No stubs, TODOs, `NotImplementedException` or skipped tests. Never weaken a test to make a
build green — fix the root cause. Do not push or publish; commits stay local.

## Line endings

Sources are normalized to LF by `.gitattributes`. Code fixes must preserve the document's own
end-of-line style: never emit a hard-coded `\n` or `\r\n`. The shared verification helpers in
`tests/EfLint.Tests/TestInfrastructure/EfLintTestResources.cs` run every code-fix test against
both LF and CRLF sources — use those helpers instead of constructing `CSharpCodeFixTest`
directly, so a fix that breaks one line-ending style is caught on any platform.
