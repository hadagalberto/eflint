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
| EFL1001 | Performance | Consulta materializada antes de filtro ou projeção | ✅ | [EFL1001](docs/rules/EFL1001.md) |
| EFL1002 | Performance | `Count()` comparado com zero/um em vez de `Any()` | ✅ | [EFL1002](docs/rules/EFL1002.md) |
| EFL1003 | Performance | Consulta somente leitura sem `AsNoTracking()` | ✅ | [EFL1003](docs/rules/EFL1003.md) |
| EFL1004 | Performance | Possível padrão N+1 em loop | — | [EFL1004](docs/rules/EFL1004.md) |
| EFL1005 | Performance | `First`/`FirstOrDefault` sem `OrderBy` | — | [EFL1005](docs/rules/EFL1005.md) |
| EFL1006 | Performance | `Skip`/`Take` sem `OrderBy` | — | [EFL1006](docs/rules/EFL1006.md) |
| EFL1007 | Performance | `SaveChanges` dentro de loop | ✅ | [EFL1007](docs/rules/EFL1007.md) |
| EFL1008 | Performance | Entidade carregada apenas para excluir ou atualizar | — | [EFL1008](docs/rules/EFL1008.md) |
| EFL1009 | Performance | Coleção grande em memória usada com `Contains` em consulta | — | [EFL1009](docs/rules/EFL1009.md) |
| EFL1010 | Performance | Múltiplos `Include`s de coleção sem `AsSplitQuery()` | ✅ | [EFL1010](docs/rules/EFL1010.md) |
| EFL2001 | Correção | Operação síncrona do EF dentro de um método `async` | ✅ | [EFL2001](docs/rules/EFL2001.md) |
| EFL2002 | Correção | Operação assíncrona do EF ignora um `CancellationToken` disponível | ✅ | [EFL2002](docs/rules/EFL2002.md) |
| EFL2003 | Correção | Método de usuário não traduzível em lambda de consulta EF | — | [EFL2003](docs/rules/EFL2003.md) |
| EFL2004 | Correção | `DbContext` capturado por lambda `Parallel`/`Task.WhenAll` | — | [EFL2004](docs/rules/EFL2004.md) |
| EFL2005 | Correção | `DbContext` registrado como singleton | — | [EFL2005](docs/rules/EFL2005.md) |
| EFL2006 | Correção | `Include` ignorado após projeção com `Select` | ✅ | [EFL2006](docs/rules/EFL2006.md) |
| EFL2007 | Correção | Comparação de strings não sargável em consulta EF | — | [EFL2007](docs/rules/EFL2007.md) |
| EFL2008 | Correção | `Update`/`Attach` redundante em entidade já rastreada | — | [EFL2008](docs/rules/EFL2008.md) |
| EFL3001 | Segurança | SQL bruto construído com interpolação ou concatenação de valores não constantes (injeção de SQL) | ✅ | [EFL3001](docs/rules/EFL3001.md) |
| EFL3002 | Segurança | SQL interpolado achatado em `string` antes de chegar a uma API de SQL bruto | — | [EFL3002](docs/rules/EFL3002.md) |

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

`samples/EfLint.Sample` viola intencionalmente todas as regras acima. Ele está fora da
configuração de build da solução para que `dotnet build -warnaserror` permaneça limpo; o teste
de integração compila o exemplo separadamente e valida o conjunto exato de diagnósticos.

## Licença

[MIT](LICENSE) — Hadagalberto Junior.
