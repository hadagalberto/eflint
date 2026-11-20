using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace EfLint.Infrastructure;

/// <summary>
/// IOperation-based helpers for walking EF query chains. Used by the query
/// rules to prove that a receiver really originates from an EF Core
/// <see cref="Microsoft.EntityFrameworkCore.DbContext"/> instead of relying on
/// method names or source text.
/// </summary>
internal static class QueryChain
{
    private const int MaxChainDepth = 64;

    private static readonly HashSet<string> QueryOperatorNames = new()
    {
        "AsQueryable", "Where", "Select", "SelectMany", "OrderBy", "OrderByDescending",
        "ThenBy", "ThenByDescending", "Skip", "Take", "Include", "ThenInclude",
        "AsNoTracking", "AsNoTrackingWithIdentityResolution", "AsTracking",
        "IgnoreAutoIncludes", "Distinct", "Reverse", "GroupBy", "Join", "GroupJoin",
        "Cast", "OfType", "DefaultIfEmpty", "Concat", "Union", "Except", "Intersect",
        "AsSplitQuery", "AsSingleQuery",
    };

    private static readonly HashSet<string> MaterializerNames = new()
    {
        "ToList", "ToListAsync", "ToArray", "ToArrayAsync", "ToDictionary", "ToDictionaryAsync",
        "ToHashSet", "ToHashSetAsync", "ToLookup", "First", "FirstAsync", "FirstOrDefault",
        "FirstOrDefaultAsync", "Single", "SingleAsync", "SingleOrDefault", "SingleOrDefaultAsync",
        "Last", "LastAsync", "LastOrDefault", "LastOrDefaultAsync", "Count", "CountAsync",
        "LongCount", "LongCountAsync", "Any", "AnyAsync", "All", "AllAsync", "Sum", "SumAsync",
        "Min", "MinAsync", "Max", "MaxAsync", "Average", "AverageAsync", "Aggregate",
        "ExecuteDelete", "ExecuteDeleteAsync", "ExecuteUpdate", "ExecuteUpdateAsync",
        "ElementAt", "ElementAtOrDefault", "Contains",
    };

    /// <summary>True when the operation is a query rooted in a DbContext/DbSet.</summary>
    public static bool IsEfQuery(IOperation operation, EfSymbols symbols)
    {
        if (!symbols.HasEntityFrameworkCore || !symbols.IsQueryableType(operation.Type))
        {
            return false;
        }

        var current = RawSqlHelpers.Unwrap(operation);
        for (var depth = 0; depth < MaxChainDepth && current is not null; depth++)
        {
            if (IsQueryRoot(current, symbols))
            {
                return true;
            }

            switch (current)
            {
                case IInvocationOperation invocation:
                    var target = invocation.TargetMethod;
                    if (!IsQueryOperator(target, symbols) && !ReturnsQuery(target, symbols))
                    {
                        return false;
                    }

                    if (invocation.Instance is not null)
                    {
                        current = RawSqlHelpers.Unwrap(invocation.Instance);
                        continue;
                    }

                    var source = invocation.Arguments.FirstOrDefault(argument => symbols.IsQueryableType(argument.Parameter?.Type));
                    if (source is null)
                    {
                        return false;
                    }

                    current = RawSqlHelpers.Unwrap(source.Value);
                    continue;

                case IMemberReferenceOperation member:
                    if (member.Instance is null)
                    {
                        return false;
                    }

                    current = RawSqlHelpers.Unwrap(member.Instance);
                    continue;

                case IConditionalOperation conditional:
                    return (conditional.WhenTrue is not null && IsEfQuery(conditional.WhenTrue, symbols))
                        || (conditional.WhenFalse is not null && IsEfQuery(conditional.WhenFalse, symbols));

                default:
                    return false;
            }
        }

        return false;
    }

    /// <summary>True when the method consumes an EF query and produces a scalar or collection.</summary>
    public static bool IsMaterializer(IMethodSymbol method, EfSymbols symbols)
    {
        if (!MaterializerNames.Contains(method.Name))
        {
            return false;
        }

        if (ReturnsQuery(method, symbols))
        {
            return false;
        }

        var containing = method.ContainingType;
        if (containing is null)
        {
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(containing, symbols.EntityFrameworkQueryableExtensions)
            || SymbolEqualityComparer.Default.Equals(containing, symbols.RelationalQueryableExtensions))
        {
            return true;
        }

        var ns = containing.ContainingNamespace;
        return (containing.Name == "Queryable" || containing.Name == "Enumerable")
            && ns?.Name == "Linq"
            && ns.ContainingNamespace?.Name == "System";
    }

    /// <summary>Finds the nearest materialization operation above a query expression.</summary>
    public static IInvocationOperation? FindMaterialization(IOperation operation, EfSymbols symbols)
    {
        IOperation current = operation;
        while (current.Parent is not null)
        {
            var parent = current.Parent;
            if (parent is IInvocationOperation invocation
                && IsMaterializer(invocation.TargetMethod, symbols)
                && (ReferenceEquals(invocation.Instance, current)
                    || invocation.Arguments.Any(argument => ReferenceEquals(argument.Value, current))))
            {
                return invocation;
            }

            current = parent;
        }

        return null;
    }

    private static bool IsQueryRoot(IOperation operation, EfSymbols symbols)
    {
        if (symbols.IsDbSetType(operation.Type))
        {
            return true;
        }

        if (operation is IMemberReferenceOperation member
            && symbols.IsDbContextType(member.Instance?.Type)
            && (symbols.IsDbSetType(member.Type) || symbols.IsQueryableType(member.Type)))
        {
            return true;
        }

        if (operation is IInvocationOperation invocation
            && invocation.TargetMethod.Name == "Set"
            && (symbols.IsDbContextType(invocation.Instance?.Type)
                || SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingType?.OriginalDefinition, symbols.DbContext)))
        {
            return true;
        }

        return false;
    }

    private static bool IsQueryOperator(IMethodSymbol method, EfSymbols symbols)
    {
        if (!QueryOperatorNames.Contains(method.Name))
        {
            return false;
        }

        var containing = method.ContainingType;
        if (SymbolEqualityComparer.Default.Equals(containing, symbols.EntityFrameworkQueryableExtensions)
            || SymbolEqualityComparer.Default.Equals(containing, symbols.RelationalQueryableExtensions))
        {
            return true;
        }

        var ns = containing?.ContainingNamespace;
        return containing?.Name == "Queryable" && ns?.Name == "Linq" && ns.ContainingNamespace?.Name == "System";
    }

    private static bool ReturnsQuery(IMethodSymbol method, EfSymbols symbols)
    {
        return symbols.IsQueryableType(method.ReturnType) || symbols.IsDbSetType(method.ReturnType);
    }
}
