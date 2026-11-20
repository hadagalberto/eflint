# EfLint

Roslyn analyzers and code fixes for Entity Framework Core. EfLint catches EF Core performance, correctness and security pitfalls at compile time.

## Install

```
dotnet add package EfLint
```

## Rules

| ID | Category | Summary |
| --- | --- | --- |
| EFL3001 | Security | Raw SQL built from interpolated or concatenated non-constant values (SQL injection). |
| EFL3002 | Security | Interpolated SQL flattened into a `string` before being passed to a raw SQL API. |

All rules are configurable through `.editorconfig`, for example:

```ini
dotnet_diagnostic.EFL3001.severity = error
eflint.provider = postgres
```
