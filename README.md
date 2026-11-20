# EfLint

[![CI](https://github.com/hadagalberto/eflint/actions/workflows/ci.yml/badge.svg)](https://github.com/hadagalberto/eflint/actions/workflows/ci.yml)

Roslyn analyzers and code fixes that catch Entity Framework Core performance, correctness
and security pitfalls at compile time. Works in Visual Studio, Rider, VS Code and
`dotnet build`; ships as a single analyzer NuGet package.

## Install

```shell
dotnet add package EfLint
```

## Rules

| ID | Category | Description | Fix | Docs |
| --- | --- | --- | --- | --- |
| EFL3001 | Security | Raw SQL built from interpolated or concatenated non-constant values (SQL injection) | ✅ | [EFL3001](docs/rules/EFL3001.md) |
| EFL3002 | Security | Interpolated SQL flattened to a `string` before reaching a raw SQL API | — | [EFL3002](docs/rules/EFL3002.md) |

Rules EFL1xxx (performance) and EFL2xxx (correctness) are planned for later milestones.

EfLint resolves EF Core types by symbol. If EF Core is not referenced, the analyzers do
nothing.

## Configuration

Severities use the standard Roslyn `.editorconfig` keys:

```ini
dotnet_diagnostic.EFL3001.severity = error
```

EfLint-specific options:

```ini
eflint.EFL3001.enabled = false
eflint.EFL3002.enabled = true
eflint.provider = postgres   # postgres | sqlserver | sqlite
```

## Development

Requires the .NET 8 SDK (pinned by `global.json`).

```shell
dotnet build EfLint.sln
dotnet test EfLint.sln
dotnet pack src/EfLint.Package/EfLint.Package.csproj -c Release
```

`samples/EfLint.Sample` intentionally violates the implemented rules and is excluded from
warnings-as-errors; the integration test builds it and asserts the exact diagnostic set.

## License

[MIT](LICENSE) — Hadagalberto Junior.
