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
| EFL2001 | Correctness | Synchronous EF operation inside an `async` method | ✅ | [EFL2001](docs/rules/EFL2001.md) |
| EFL2002 | Correctness | Async EF operation ignores an available `CancellationToken` | ✅ | [EFL2002](docs/rules/EFL2002.md) |
| EFL2003 | Correctness | Non-translatable user method inside an EF query lambda | — | [EFL2003](docs/rules/EFL2003.md) |
| EFL2004 | Correctness | `DbContext` captured by a `Parallel`/`Task.WhenAll` lambda | — | [EFL2004](docs/rules/EFL2004.md) |
| EFL2005 | Correctness | `DbContext` registered as a singleton | — | [EFL2005](docs/rules/EFL2005.md) |
| EFL2006 | Correctness | `Include` ignored after a `Select` projection | ✅ | [EFL2006](docs/rules/EFL2006.md) |
| EFL2007 | Correctness | Non-sargable string comparison inside an EF query | — | [EFL2007](docs/rules/EFL2007.md) |
| EFL2008 | Correctness | Redundant `Update`/`Attach` on an already tracked entity | — | [EFL2008](docs/rules/EFL2008.md) |
| EFL3001 | Security | Raw SQL built from interpolated or concatenated non-constant values (SQL injection) | ✅ | [EFL3001](docs/rules/EFL3001.md) |
| EFL3002 | Security | Interpolated SQL flattened to a `string` before reaching a raw SQL API | — | [EFL3002](docs/rules/EFL3002.md) |

Rules EFL1xxx (performance) are planned for a later milestone.

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
