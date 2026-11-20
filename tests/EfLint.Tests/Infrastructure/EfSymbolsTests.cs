using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EfLint.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Infrastructure;

public class EfSymbolsTests
{
    [Fact]
    public async Task WithEfCoreReference_ResolvesAllSymbols()
    {
        var compilation = await CreateCompilationAsync(EfLintTestResources.EfCore);
        var symbols = EfSymbols.Create(compilation);

        Assert.True(symbols.HasEntityFrameworkCore);
        Assert.True(symbols.HasRelational);
        Assert.NotNull(symbols.DbContext);
        Assert.NotNull(symbols.DbSet);
        Assert.NotNull(symbols.IQueryable);
        Assert.NotNull(symbols.DatabaseFacade);
        Assert.NotNull(symbols.FormattableString);
        Assert.NotNull(symbols.EntityFrameworkQueryableExtensions);
        Assert.NotNull(symbols.RelationalQueryableExtensions);
        Assert.NotNull(symbols.RelationalDatabaseFacadeExtensions);
    }

    [Fact]
    public async Task Create_IsCachedPerCompilation()
    {
        // Hand-derived: every analyzer resolving EF symbols for the same
        // compilation must share one cache instance (one metadata lookup).
        var compilation = await CreateCompilationAsync(EfLintTestResources.EfCore);
        var first = EfSymbols.Create(compilation);
        var second = EfSymbols.Create(compilation);

        Assert.Same(first, second);
    }

    [Fact]
    public async Task WithoutEfCoreReference_NoOp()
    {
        var compilation = await CreateCompilationAsync(ReferenceAssemblies.Net.Net80);
        var symbols = EfSymbols.Create(compilation);

        Assert.False(symbols.HasEntityFrameworkCore);
        Assert.False(symbols.HasRelational);
        Assert.Null(symbols.DbContext);
        Assert.Null(symbols.DbSet);
    }

    [Fact]
    public async Task RawSqlMethods_AreMappedToParameterizedCounterparts()
    {
        var compilation = await CreateCompilationAsync(EfLintTestResources.EfCore);
        var symbols = EfSymbols.Create(compilation);

        var relationalQueryable = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.RelationalQueryableExtensions")!;
        var relationalDatabase = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions")!;

        AssertMapsTo(symbols, relationalQueryable.GetMembers("FromSqlRaw").OfType<IMethodSymbol>().First(), "FromSql");
        AssertMapsTo(symbols, relationalDatabase.GetMembers("ExecuteSqlRaw").OfType<IMethodSymbol>().First(), "ExecuteSql");
        AssertMapsTo(symbols, relationalDatabase.GetMembers("ExecuteSqlRawAsync").OfType<IMethodSymbol>().First(), "ExecuteSqlAsync");
        AssertMapsTo(symbols, relationalDatabase.GetMembers("SqlQueryRaw").OfType<IMethodSymbol>().First(), "SqlQuery");
        Assert.False(symbols.TryGetRawSqlMethod(
            relationalDatabase.GetMembers("ExecuteSql").OfType<IMethodSymbol>().First(),
            out _));
    }

    private static void AssertMapsTo(EfSymbols symbols, IMethodSymbol method, string expected)
    {
        Assert.True(symbols.TryGetRawSqlMethod(method, out var info));
        Assert.Equal(expected, info.SafeName);
    }

    private static async Task<Compilation> CreateCompilationAsync(Microsoft.CodeAnalysis.Testing.ReferenceAssemblies references)
    {
        var metadataReferences = await references.ResolveAsync(null, CancellationToken.None);
        return CSharpCompilation.Create(
            "EfSymbolsTest",
            new[] { CSharpSyntaxTree.ParseText("public class Placeholder { }") },
            metadataReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
