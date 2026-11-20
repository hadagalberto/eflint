using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace EfLint.Infrastructure;

/// <summary>
/// Symbol cache for the EF Core types EfLint reasons about. Created once per
/// compilation from a <c>RegisterCompilationStartAction</c>; when EF Core is not
/// referenced the cache reports <see cref="HasEntityFrameworkCore"/> as false and
/// every analyzer exits before registering any per-operation work.
/// </summary>
internal sealed class EfSymbols
{
    private EfSymbols(
        INamedTypeSymbol? dbContext,
        INamedTypeSymbol? dbSet,
        INamedTypeSymbol? iqueryable,
        INamedTypeSymbol? ienumerable,
        INamedTypeSymbol? databaseFacade,
        INamedTypeSymbol? formattableString,
        INamedTypeSymbol? entityFrameworkQueryableExtensions,
        INamedTypeSymbol? relationalQueryableExtensions,
        INamedTypeSymbol? relationalDatabaseFacadeExtensions,
        INamedTypeSymbol? cancellationToken,
        INamedTypeSymbol? dbFunctionAttribute,
        INamedTypeSymbol? dbFunctions,
        INamedTypeSymbol? parallel,
        INamedTypeSymbol? task,
        INamedTypeSymbol? serviceLifetime,
        INamedTypeSymbol? serviceCollectionServiceExtensions,
        INamedTypeSymbol? entityFrameworkServiceCollectionExtensions,
        INamedTypeSymbol? stringComparison)
    {
        DbContext = dbContext;
        DbSet = dbSet;
        IQueryable = iqueryable;
        IEnumerable = ienumerable;
        DatabaseFacade = databaseFacade;
        FormattableString = formattableString;
        EntityFrameworkQueryableExtensions = entityFrameworkQueryableExtensions;
        RelationalQueryableExtensions = relationalQueryableExtensions;
        RelationalDatabaseFacadeExtensions = relationalDatabaseFacadeExtensions;
        CancellationToken = cancellationToken;
        DbFunctionAttribute = dbFunctionAttribute;
        DbFunctions = dbFunctions;
        Parallel = parallel;
        Task = task;
        ServiceLifetime = serviceLifetime;
        ServiceCollectionServiceExtensions = serviceCollectionServiceExtensions;
        EntityFrameworkServiceCollectionExtensions = entityFrameworkServiceCollectionExtensions;
        StringComparison = stringComparison;
    }

    public INamedTypeSymbol? DbContext { get; }

    public INamedTypeSymbol? DbSet { get; }

    public INamedTypeSymbol? IQueryable { get; }

    public INamedTypeSymbol? IEnumerable { get; }

    public INamedTypeSymbol? DatabaseFacade { get; }

    public INamedTypeSymbol? FormattableString { get; }

    public INamedTypeSymbol? EntityFrameworkQueryableExtensions { get; }

    public INamedTypeSymbol? RelationalQueryableExtensions { get; }

    public INamedTypeSymbol? RelationalDatabaseFacadeExtensions { get; }

    public INamedTypeSymbol? CancellationToken { get; }

    public INamedTypeSymbol? DbFunctionAttribute { get; }

    public INamedTypeSymbol? DbFunctions { get; }

    public INamedTypeSymbol? Parallel { get; }

    public INamedTypeSymbol? Task { get; }

    public INamedTypeSymbol? ServiceLifetime { get; }

    public INamedTypeSymbol? ServiceCollectionServiceExtensions { get; }

    public INamedTypeSymbol? EntityFrameworkServiceCollectionExtensions { get; }

    public INamedTypeSymbol? StringComparison { get; }

    public bool HasEntityFrameworkCore => DbContext is not null && DbSet is not null;

    public bool HasRelational => RelationalQueryableExtensions is not null && RelationalDatabaseFacadeExtensions is not null;

    public bool HasDependencyInjection => ServiceLifetime is not null
        && EntityFrameworkServiceCollectionExtensions is not null
        && ServiceCollectionServiceExtensions is not null;

    public static EfSymbols Create(Compilation compilation)
    {
        return new EfSymbols(
            dbContext: compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContext"),
            dbSet: compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbSet`1"),
            iqueryable: compilation.GetTypeByMetadataName("System.Linq.IQueryable`1"),
            ienumerable: compilation.GetTypeByMetadataName("System.Collections.IEnumerable"),
            databaseFacade: compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade"),
            formattableString: compilation.GetTypeByMetadataName("System.FormattableString"),
            entityFrameworkQueryableExtensions: compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions"),
            relationalQueryableExtensions: compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.RelationalQueryableExtensions"),
            relationalDatabaseFacadeExtensions: compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions"),
            cancellationToken: compilation.GetTypeByMetadataName("System.Threading.CancellationToken"),
            dbFunctionAttribute: compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbFunctionAttribute"),
            dbFunctions: compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbFunctions"),
            parallel: compilation.GetTypeByMetadataName("System.Threading.Tasks.Parallel"),
            task: compilation.GetTypeByMetadataName("System.Threading.Tasks.Task"),
            serviceLifetime: compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.ServiceLifetime"),
            serviceCollectionServiceExtensions: compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions"),
            entityFrameworkServiceCollectionExtensions: compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.EntityFrameworkServiceCollectionExtensions"),
            stringComparison: compilation.GetTypeByMetadataName("System.StringComparison"));
    }

    public bool IsDbContextType(ITypeSymbol? type)
    {
        if (DbContext is null || type is null)
        {
            return false;
        }

        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, DbContext))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsDbSetType(ITypeSymbol? type)
    {
        return DbSet is not null
            && type is INamedTypeSymbol named
            && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, DbSet);
    }

    public bool IsQueryableType(ITypeSymbol? type)
    {
        if (IQueryable is null || type is null)
        {
            return false;
        }

        if (IsGenericDefinition(type, IQueryable))
        {
            return true;
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (IsGenericDefinition(iface, IQueryable))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsCancellationTokenType(ITypeSymbol? type)
    {
        return CancellationToken is not null
            && SymbolEqualityComparer.Default.Equals(CancellationToken, type);
    }

    public bool HasDbFunctionAttribute(IMethodSymbol method)
    {
        if (DbFunctionAttribute is null)
        {
            return false;
        }

        foreach (var attribute in method.GetAttributes())
        {
            if (attribute.AttributeClass is not null
                && SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, DbFunctionAttribute))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsEfAsyncQueryMethod(IMethodSymbol method)
    {
        var containing = method.ContainingType;
        if (containing is null || !method.Name.EndsWith("Async", System.StringComparison.Ordinal))
        {
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(containing, EntityFrameworkQueryableExtensions)
            || SymbolEqualityComparer.Default.Equals(containing, RelationalQueryableExtensions))
        {
            return HasCancellationTokenParameter(method);
        }

        return false;
    }

    public static bool HasCancellationTokenParameter(IMethodSymbol method)
    {
        foreach (var parameter in method.Parameters)
        {
            if (parameter.Type.Name == "CancellationToken"
                && parameter.Type.ContainingNamespace?.Name == "Threading"
                && parameter.Type.ContainingNamespace.ContainingNamespace?.Name == "System")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when an argument expression is <c>ServiceLifetime.Singleton</c>.</summary>
    public bool IsSingletonLifetime(IOperation? operation)
    {
        if (operation is null)
        {
            return false;
        }

        var value = RawSqlHelpers.Unwrap(operation);
        if (value is IFieldReferenceOperation field
            && field.Field.Name == "Singleton"
            && SymbolEqualityComparer.Default.Equals(field.Field.ContainingType, ServiceLifetime))
        {
            return true;
        }

        return value.ConstantValue.HasValue
            && value.ConstantValue.Value is int number
            && number == 0
            && ServiceLifetime is not null;
    }

    /// <summary>
    /// Recognizes the raw SQL extension methods verified against EF Core 8:
    /// RelationalQueryableExtensions.FromSqlRaw and
    /// RelationalDatabaseFacadeExtensions.ExecuteSqlRaw / ExecuteSqlRawAsync / SqlQueryRaw.
    /// </summary>
    public bool TryGetRawSqlMethod(IMethodSymbol method, out RawSqlMethodInfo info)
    {
        info = default;

        var definition = method.ReducedFrom?.OriginalDefinition ?? method.OriginalDefinition;
        var containing = definition.ContainingType;
        if (containing is null)
        {
            return false;
        }

        string? safeName = null;
        if (SymbolEqualityComparer.Default.Equals(containing, RelationalQueryableExtensions)
            && definition.Name == "FromSqlRaw")
        {
            safeName = "FromSql";
        }
        else if (SymbolEqualityComparer.Default.Equals(containing, RelationalDatabaseFacadeExtensions))
        {
            switch (definition.Name)
            {
                case "ExecuteSqlRaw":
                    safeName = "ExecuteSql";
                    break;
                case "ExecuteSqlRawAsync":
                    safeName = "ExecuteSqlAsync";
                    break;
                case "SqlQueryRaw":
                    safeName = "SqlQuery";
                    break;
            }
        }

        if (safeName is null)
        {
            return false;
        }

        info = new RawSqlMethodInfo(definition.Name, safeName);
        return true;
    }

    private static bool IsGenericDefinition(ITypeSymbol candidate, INamedTypeSymbol definition)
    {
        return candidate is INamedTypeSymbol named
            && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, definition);
    }
}

internal readonly struct RawSqlMethodInfo
{
    public RawSqlMethodInfo(string name, string safeName)
    {
        Name = name;
        SafeName = safeName;
    }

    public string Name { get; }

    public string SafeName { get; }
}
