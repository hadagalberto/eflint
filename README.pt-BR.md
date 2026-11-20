# EfLint

[![CI](https://github.com/hadagalberto/eflint/actions/workflows/ci.yml/badge.svg)](https://github.com/hadagalberto/eflint/actions/workflows/ci.yml)

Analisadores e correções Roslyn que detectam problemas de performance, correção e segurança
do Entity Framework Core em tempo de compilação. Funciona no Visual Studio, Rider, VS Code e
`dotnet build`; publicado como um único pacote NuGet de analisador.

## Instalação

```shell
dotnet add package EfLint
```

## Regras

| ID | Categoria | Descrição | Correção | Documentação |
| --- | --- | --- | --- | --- |
| EFL3001 | Segurança | SQL bruto construído com interpolação ou concatenação de valores não constantes (injeção de SQL) | ✅ | [EFL3001](docs/rules/EFL3001.md) |
| EFL3002 | Segurança | SQL interpolado achatado em `string` antes de chegar a uma API de SQL bruto | — | [EFL3002](docs/rules/EFL3002.md) |

As regras EFL1xxx (performance) e EFL2xxx (correção) estão previstas para marcos futuros.

O EfLint resolve os tipos do EF Core por símbolo. Se o EF Core não estiver referenciado, os
analisadores não fazem nada.

## Configuração

Severidades usam as chaves padrão do Roslyn em `.editorconfig`:

```ini
dotnet_diagnostic.EFL3001.severity = error
```

Opções específicas do EfLint:

```ini
eflint.EFL3001.enabled = false
eflint.EFL3002.enabled = true
eflint.provider = postgres   # postgres | sqlserver | sqlite
```

## Desenvolvimento

Requer o SDK .NET 8 (fixado por `global.json`).

```shell
dotnet build EfLint.sln
dotnet test EfLint.sln
dotnet pack src/EfLint.Package/EfLint.Package.csproj -c Release
```

`samples/EfLint.Sample` viola intencionalmente as regras implementadas e está fora de
warnings-as-errors; o teste de integração compila o exemplo e valida o conjunto exato de
diagnósticos.

## Licença

[MIT](LICENSE) — Hadagalberto Junior.
