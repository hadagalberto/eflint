using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace EfLint.Infrastructure;

/// <summary>Helpers for lambdas passed to EF Core query operators.</summary>
internal static class QueryLambdaHelpers
{
    private static readonly HashSet<string> LambdaOperators = new()
    {
        "Where", "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending",
        "Select", "SelectMany", "Any", "All", "Count", "LongCount", "First",
        "FirstOrDefault", "Single", "SingleOrDefault", "Last", "LastOrDefault",
        "Sum", "Min", "Max", "Average", "Contains",
    };

    /// <summary>
    /// True when an operation is inside a lambda passed to a query operator whose
    /// source is an EF Core query rooted in a DbContext.
    /// </summary>
    public static bool IsInsideEfQueryLambda(IOperation operation, EfSymbols symbols)
    {
        if (!symbols.HasEntityFrameworkCore)
        {
            return false;
        }

        var current = operation;
        while (current.Parent is not null)
        {
            current = current.Parent;
            if (current is not IAnonymousFunctionOperation)
            {
                continue;
            }

            var parent = current.Parent;
            while (parent is IConversionOperation or IParenthesizedOperation)
            {
                parent = parent.Parent;
            }

            if (parent is IArgumentOperation argument
                && argument.Parent is IInvocationOperation invocation
                && LambdaOperators.Contains(invocation.TargetMethod.Name))
            {
                var source = invocation.Instance
                    ?? invocation.Arguments.FirstOrDefault(a => symbols.IsQueryableType(a.Parameter?.Type))?.Value;
                if (source is not null && QueryChain.IsEfQuery(source, symbols))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
