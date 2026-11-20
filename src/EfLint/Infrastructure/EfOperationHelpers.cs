using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace EfLint.Infrastructure;

/// <summary>IOperation helpers shared by the correctness analyzers.</summary>
internal static class EfOperationHelpers
{
    // Operations for one function body are reused within an analyzer pass. Index
    // assignments once per body instead of rescanning every descendant for each
    // local reference (the old behaviour was O(assignments x references)).
    private static readonly ConditionalWeakTable<IOperation, Dictionary<ILocalSymbol, List<(int Position, IOperation Value)>>> LastValueIndex = new();

    /// <summary>Returns the innermost function (method, lambda or local function) containing an operation.</summary>
    public static IOperation? FindEnclosingFunction(IOperation operation)
    {
        var current = operation;
        while (current.Parent is not null)
        {
            current = current.Parent;
            switch (current)
            {
                case IAnonymousFunctionOperation:
                case ILocalFunctionOperation:
                case IMethodBodyOperation:
                case IConstructorBodyOperation:
                    return current;
            }
        }

        return null;
    }

    /// <summary>True when the innermost function containing an operation is async.</summary>
    public static bool IsInsideAsyncFunction(IOperation operation)
    {
        var function = FindEnclosingFunction(operation);
        return GetFunctionSymbol(function) is IMethodSymbol method && method.IsAsync;
    }

    public static ISymbol? GetFunctionSymbol(IOperation? function)
    {
        return function switch
        {
            IAnonymousFunctionOperation lambda => lambda.Symbol,
            ILocalFunctionOperation local => local.Symbol,
            IMethodBodyOperation or IConstructorBodyOperation =>
                function.SemanticModel?.GetDeclaredSymbol(function.Syntax) as IMethodSymbol,
            _ => null,
        };
    }

    /// <summary>
    /// Finds a CancellationToken that is lexically in scope at the given operation.
    /// Uses the semantic model's LookupSymbols instead of a syntactic scan: a token
    /// declared earlier in a sibling block is not in scope, and passing it to the
    /// code fix would produce CS0103.
    /// </summary>
    public static ISymbol? FindCancellationTokenInScope(IOperation operation, EfSymbols symbols)
    {
        var model = operation.SemanticModel;
        if (model is null)
        {
            return null;
        }

        var usePosition = operation.Syntax.SpanStart;
        ISymbol? best = null;
        var bestPosition = -1;

        foreach (var candidate in model.LookupSymbols(usePosition))
        {
            ITypeSymbol? type = candidate switch
            {
                ILocalSymbol local => local.Type,
                IParameterSymbol parameter => parameter.Type,
                _ => null,
            };

            if (type is null || !symbols.IsCancellationTokenType(type))
            {
                continue;
            }

            var declarationPosition = GetDeclarationPosition(candidate);
            if (declarationPosition <= usePosition && declarationPosition > bestPosition)
            {
                best = candidate;
                bestPosition = declarationPosition;
            }
        }

        return best;
    }

    private static int GetDeclarationPosition(ISymbol symbol)
    {
        return symbol.DeclaringSyntaxReferences.Length > 0
            ? symbol.DeclaringSyntaxReferences[0].GetSyntax().SpanStart
            : -1;
    }

    /// <summary>
    /// Returns the last value assigned to a specific local before <paramref name="usePosition"/>
    /// inside a function body. Used for conservative intra-method data flow.
    /// </summary>
    public static IOperation? FindLastValueForSymbol(IOperation body, ILocalSymbol local, int usePosition)
    {
        if (!GetLastValueIndex(body).TryGetValue(local, out var values))
        {
            return null;
        }

        IOperation? best = null;
        var bestPosition = -1;
        foreach (var (position, value) in values)
        {
            if (position <= usePosition && position > bestPosition)
            {
                best = value;
                bestPosition = position;
            }
        }

        return best;
    }

    private static Dictionary<ILocalSymbol, List<(int Position, IOperation Value)>> GetLastValueIndex(IOperation body)
    {
        return LastValueIndex.GetValue(body, static bodyOperation =>
        {
            var index = new Dictionary<ILocalSymbol, List<(int Position, IOperation Value)>>(SymbolEqualityComparer.Default);
            foreach (var node in bodyOperation.DescendantsAndSelf())
            {
                ILocalSymbol? local = null;
                IOperation? value = null;
                switch (node)
                {
                    case IVariableDeclaratorOperation declarator when declarator.Initializer is not null:
                        local = declarator.Symbol;
                        value = declarator.Initializer.Value;
                        break;

                    case ISimpleAssignmentOperation assignment when assignment.Target is ILocalReferenceOperation target:
                        local = target.Local;
                        value = assignment.Value;
                        break;

                    case ICompoundAssignmentOperation compound when compound.Target is ILocalReferenceOperation compoundTarget:
                        local = compoundTarget.Local;
                        value = compound.Value;
                        break;
                }

                if (local is null || value is null)
                {
                    continue;
                }

                if (!index.TryGetValue(local, out var entries))
                {
                    entries = new List<(int Position, IOperation Value)>();
                    index.Add(local, entries);
                }

                entries.Add((node.Syntax.SpanStart, value));
            }

            return index;
        });
    }

    /// <summary>
    /// True when the method is declared in source (a user method) rather than in a
    /// referenced assembly. DbFunction-marked methods are excluded by callers.
    /// </summary>
    public static bool IsUserDefinedMethod(IMethodSymbol method)
    {
        return method.DeclaringSyntaxReferences.Length > 0;
    }

    /// <summary>True when a symbol is declared outside the given lambda.</summary>
    public static bool IsCaptured(IOperation lambda, ISymbol symbol)
    {
        var lambdaSpan = lambda.Syntax.Span;
        foreach (var location in symbol.DeclaringSyntaxReferences)
        {
            if (lambdaSpan.Contains(location.GetSyntax().Span))
            {
                return false;
            }
        }

        return true;
    }
}
