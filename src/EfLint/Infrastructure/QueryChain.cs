using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
        return IsEfQuery(operation, symbols, operation.SemanticModel, NewVisitedMethods());
    }

    private static bool IsEfQuery(
        IOperation operation,
        EfSymbols symbols,
        SemanticModel? model,
        HashSet<IMethodSymbol> visitedMethods)
    {
        var current = RawSqlHelpers.Unwrap(operation);
        if (!symbols.HasEntityFrameworkCore || !symbols.IsQueryableType(current.Type))
        {
            return false;
        }

        for (var depth = 0; depth < MaxChainDepth && current is not null; depth++)
        {
            if (IsQueryRoot(current, symbols))
            {
                return true;
            }

            if (TryGetLocalValue(current, out var localValue))
            {
                current = RawSqlHelpers.Unwrap(localValue!);
                continue;
            }

            switch (current)
            {
                case IInvocationOperation invocation:
                    if (TryGetFactoryReturnValues(invocation, symbols, model, visitedMethods, out var returns, out _))
                    {
                        // A source-declared factory is an EF query only when every returned
                        // expression is itself proven to originate from a DbContext.
                        return returns.Count > 0 && returns.All(value =>
                            IsEfQuery(value, symbols, value.SemanticModel ?? model, visitedMethods));
                    }

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
                    return (conditional.WhenTrue is not null && IsEfQuery(conditional.WhenTrue, symbols, model, visitedMethods))
                        || (conditional.WhenFalse is not null && IsEfQuery(conditional.WhenFalse, symbols, model, visitedMethods));

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

    /// <summary>
    /// Walks a query chain down to the DbContext instance it originates from.
    /// Returns null when the chain is not rooted in a DbContext.
    /// </summary>
    public static IOperation? FindContextRoot(IOperation operation, EfSymbols symbols)
    {
        return FindContextRoot(operation, symbols, operation.SemanticModel, NewVisitedMethods(), null);
    }

    private static IOperation? FindContextRoot(
        IOperation operation,
        EfSymbols symbols,
        SemanticModel? model,
        HashSet<IMethodSymbol> visitedMethods,
        IReadOnlyDictionary<IParameterSymbol, IOperation>? substitutions)
    {
        var current = RawSqlHelpers.Unwrap(operation);
        if (!symbols.HasEntityFrameworkCore || !symbols.IsQueryableType(current.Type))
        {
            return null;
        }

        for (var depth = 0; depth < MaxChainDepth && current is not null; depth++)
        {
            if (current is IMemberReferenceOperation member
                && symbols.IsDbContextType(member.Instance?.Type)
                && (symbols.IsDbSetType(member.Type) || symbols.IsQueryableType(member.Type)))
            {
                return ApplySubstitutions(RawSqlHelpers.Unwrap(member.Instance!), substitutions);
            }

            if (current is IInvocationOperation invocation
                && invocation.TargetMethod.Name == "Set"
                && symbols.IsDbContextType(invocation.Instance?.Type))
            {
                return ApplySubstitutions(RawSqlHelpers.Unwrap(invocation.Instance!), substitutions);
            }

            if (TryGetLocalValue(current, out var localValue))
            {
                current = RawSqlHelpers.Unwrap(localValue!);
                continue;
            }

            if (current is IParameterReferenceOperation parameterReference
                && substitutions is not null
                && substitutions.TryGetValue(parameterReference.Parameter, out var substituted))
            {
                current = RawSqlHelpers.Unwrap(substituted);
                continue;
            }

            switch (current)
            {
                case IInvocationOperation currentInvocation:
                    if (TryGetFactoryReturnValues(currentInvocation, symbols, model, visitedMethods, out var returns, out var factorySubstitutions))
                    {
                        foreach (var returned in returns)
                        {
                            var root = FindContextRoot(returned, symbols, returned.SemanticModel ?? model, visitedMethods, factorySubstitutions);
                            if (root is not null)
                            {
                                return root;
                            }
                        }

                        return null;
                    }

                    if (currentInvocation.Instance is not null)
                    {
                        current = RawSqlHelpers.Unwrap(currentInvocation.Instance);
                        continue;
                    }

                    var source = currentInvocation.Arguments.FirstOrDefault(argument => symbols.IsQueryableType(argument.Parameter?.Type));
                    if (source is null)
                    {
                        return null;
                    }

                    current = RawSqlHelpers.Unwrap(source.Value);
                    continue;

                case IMemberReferenceOperation currentMember:
                    if (currentMember.Instance is null)
                    {
                        return null;
                    }

                    current = RawSqlHelpers.Unwrap(currentMember.Instance);
                    continue;

                case IConditionalOperation conditional:
                    if (conditional.WhenTrue is not null)
                    {
                        var root = FindContextRoot(conditional.WhenTrue, symbols, model, visitedMethods, substitutions);
                        if (root is not null)
                        {
                            return root;
                        }
                    }

                    return conditional.WhenFalse is null
                        ? null
                        : FindContextRoot(conditional.WhenFalse, symbols, model, visitedMethods, substitutions);

                default:
                    return null;
            }
        }

        return null;
    }

    /// <summary>True when the invocation is a LINQ/EF query operator with the given name.</summary>
    public static bool IsQueryOperatorNamed(IMethodSymbol method, EfSymbols symbols, string name)
    {
        return method.Name == name && IsQueryOperator(method, symbols);
    }

    /// <summary>True when the argument value is a lambda expression.</summary>
    public static bool IsLambda(IArgumentOperation? argument)
    {
        return argument is not null && RawSqlHelpers.Unwrap(argument.Value) is IAnonymousFunctionOperation;
    }

    private static IOperation ApplySubstitutions(
        IOperation operation,
        IReadOnlyDictionary<IParameterSymbol, IOperation>? substitutions)
    {
        var current = operation;
        for (var depth = 0; depth < MaxChainDepth; depth++)
        {
            if (current is not IParameterReferenceOperation parameter
                || substitutions is null
                || !substitutions.TryGetValue(parameter.Parameter, out var replacement))
            {
                break;
            }

            current = RawSqlHelpers.Unwrap(replacement);
        }

        return current;
    }

    private static bool TryGetLocalValue(IOperation current, out IOperation? value)
    {
        value = null;
        if (current is not ILocalReferenceOperation local)
        {
            return false;
        }

        var function = EfOperationHelpers.FindEnclosingFunction(local);
        if (function is null)
        {
            return false;
        }

        value = EfOperationHelpers.FindLastValueForSymbol(function, local.Local, local.Syntax.SpanStart);
        return value is not null;
    }

    /// <summary>
    /// Recognizes a source-declared method/property whose body returns an IQueryable.
    /// Such a factory is only treated as an EF query when every returned expression
    /// can itself be proven to originate from a DbContext.
    /// </summary>
    private static bool TryGetFactoryReturnValues(
        IInvocationOperation invocation,
        EfSymbols symbols,
        SemanticModel? model,
        HashSet<IMethodSymbol> visitedMethods,
        out IReadOnlyList<IOperation> returns,
        out IReadOnlyDictionary<IParameterSymbol, IOperation> substitutions)
    {
        returns = System.Array.Empty<IOperation>();
        substitutions = new Dictionary<IParameterSymbol, IOperation>(SymbolEqualityComparer.Default);
        var method = invocation.TargetMethod.OriginalDefinition;
        if (!ReturnsQuery(method, symbols)
            || method.DeclaringSyntaxReferences.IsDefaultOrEmpty
            || !visitedMethods.Add(method))
        {
            return false;
        }

        var syntaxReference = method.DeclaringSyntaxReferences[0];
        var modelForDeclaration = GetModelForSyntaxTree(syntaxReference.SyntaxTree, model, invocation.SemanticModel);
        if (modelForDeclaration is null)
        {
            return false;
        }

        var declaration = syntaxReference.GetSyntax();
        var values = new List<IOperation>();
        foreach (var syntax in declaration.DescendantNodes())
        {
            SyntaxNode? expression = syntax switch
            {
                ReturnStatementSyntax { Expression: not null } statement => statement.Expression,
                ArrowExpressionClauseSyntax arrow => arrow.Expression,
                _ => null,
            };

            if (expression is null)
            {
                continue;
            }

            var returned = modelForDeclaration.GetOperation(expression);
            if (returned is not null)
            {
                values.Add(returned);
            }
        }

        if (values.Count == 0)
        {
            return false;
        }

        returns = values;
        var mapped = new Dictionary<IParameterSymbol, IOperation>(SymbolEqualityComparer.Default);
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter is not null)
            {
                mapped[argument.Parameter] = argument.Value;
            }
        }

        substitutions = mapped;
        return true;
    }

    private static SemanticModel? GetModelForSyntaxTree(SyntaxTree tree, SemanticModel? model, SemanticModel? fallback)
    {
        if (model is not null)
        {
            return ReferenceEquals(model.SyntaxTree, tree)
                ? model
                : model.Compilation.GetSemanticModel(tree);
        }

        return fallback is not null
            ? (ReferenceEquals(fallback.SyntaxTree, tree) ? fallback : fallback.Compilation.GetSemanticModel(tree))
            : null;
    }

    private static HashSet<IMethodSymbol> NewVisitedMethods() =>
        new(SymbolEqualityComparer.Default);

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
