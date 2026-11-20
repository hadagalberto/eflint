using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace EfLint.Infrastructure;

/// <summary>IOperation helpers shared by the correctness analyzers.</summary>
internal static class EfOperationHelpers
{
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
    /// Finds a CancellationToken parameter or local that is in scope at the given
    /// operation. Parameters are preferred, then locals declared before the use.
    /// </summary>
    public static ISymbol? FindCancellationTokenInScope(IOperation operation, EfSymbols symbols)
    {
        var function = FindEnclosingFunction(operation);
        if (function is null)
        {
            return null;
        }

        if (GetFunctionSymbol(function) is IMethodSymbol method)
        {
            foreach (var parameter in method.Parameters)
            {
                if (symbols.IsCancellationTokenType(parameter.Type))
                {
                    return parameter;
                }
            }
        }

        return FindCancellationTokenLocal(function, operation.Syntax.SpanStart, symbols);
    }

    private static ILocalSymbol? FindCancellationTokenLocal(IOperation body, int usePosition, EfSymbols symbols)
    {
        ILocalSymbol? best = null;
        var bestPosition = -1;

        foreach (var node in body.DescendantsAndSelf())
        {
            ILocalSymbol? local = null;
            if (node is IVariableDeclaratorOperation declarator
                && declarator.Syntax.SpanStart <= usePosition
                && symbols.IsCancellationTokenType(declarator.Symbol.Type))
            {
                local = declarator.Symbol;
            }

            var position = node.Syntax.SpanStart;
            if (local is not null && position <= usePosition && position > bestPosition)
            {
                best = local;
                bestPosition = position;
            }
        }

        return best;
    }

    /// <summary>
    /// Returns the last value assigned to a specific local before <paramref name="usePosition"/>
    /// inside a function body. Used for conservative intra-method data flow.
    /// </summary>
    public static IOperation? FindLastValueForSymbol(IOperation body, ILocalSymbol local, int usePosition)
    {
        IOperation? best = null;
        var bestPosition = -1;

        foreach (var node in body.DescendantsAndSelf())
        {
            IOperation? value = null;
            switch (node)
            {
                case IVariableDeclaratorOperation declarator
                    when SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) && declarator.Initializer is not null:
                    value = declarator.Initializer.Value;
                    break;

                case ISimpleAssignmentOperation assignment
                    when assignment.Target is ILocalReferenceOperation target
                        && SymbolEqualityComparer.Default.Equals(target.Local, local):
                    value = assignment.Value;
                    break;

                case ICompoundAssignmentOperation compound
                    when compound.Target is ILocalReferenceOperation compoundTarget
                        && SymbolEqualityComparer.Default.Equals(compoundTarget.Local, local):
                    value = compound.Value;
                    break;
            }

            var position = node.Syntax.SpanStart;
            if (value is not null && position <= usePosition && position > bestPosition)
            {
                best = value;
                bestPosition = position;
            }
        }

        return best;
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
