using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace EfLint.Infrastructure;

/// <summary>Shared IOperation helpers for the raw-SQL analyzers.</summary>
internal static class RawSqlHelpers
{
    public static IOperation Unwrap(IOperation operation)
    {
        var current = operation;
        while (true)
        {
            switch (current)
            {
                case IConversionOperation conversion:
                    current = conversion.Operand;
                    continue;
                case IParenthesizedOperation parenthesized:
                    current = parenthesized.Operand;
                    continue;
                case IAwaitOperation awaitOperation:
                    current = awaitOperation.Operation;
                    continue;
                default:
                    return current;
            }
        }
    }

    /// <summary>
    /// Returns the string-valued SQL argument of a raw SQL invocation. Every
    /// verified EF Core raw SQL overload has the SQL text as its first argument.
    /// </summary>
    public static IArgumentOperation? GetSqlArgument(IInvocationOperation invocation)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Type.SpecialType == SpecialType.System_String)
            {
                return argument;
            }
        }

        return invocation.Arguments.FirstOrDefault();
    }

    /// <summary>
    /// True when the SQL expression embeds at least one value that is not a
    /// compile-time constant. Interpolations, string concatenation,
    /// <c>string.Concat</c> and <c>string.Format</c> are recognized.
    /// </summary>
    public static bool ContainsNonConstantValue(IOperation operation)
    {
        var value = Unwrap(operation);

        switch (value)
        {
            case IInterpolatedStringOperation interpolated:
                foreach (var part in interpolated.Parts)
                {
                    if (part is IInterpolationOperation interpolation && !IsConstant(interpolation.Expression))
                    {
                        return true;
                    }
                }

                return false;

            case IBinaryOperation binary
                when binary.OperatorKind == BinaryOperatorKind.Add && binary.Type?.SpecialType == SpecialType.System_String:
                return !IsConstant(binary.LeftOperand) || !IsConstant(binary.RightOperand);

            case IInvocationOperation invocation when IsStringComposition(invocation.TargetMethod):
                return invocation.Arguments.Any(argument => !IsConstant(argument.Value));

            case IConditionalOperation conditional:
                return (conditional.WhenTrue is not null && ContainsNonConstantValue(conditional.WhenTrue))
                    || (conditional.WhenFalse is not null && ContainsNonConstantValue(conditional.WhenFalse));

            default:
                return false;
        }
    }

    private static bool IsConstant(IOperation operation)
    {
        return Unwrap(operation).ConstantValue.HasValue;
    }

    private static bool IsStringComposition(IMethodSymbol method)
    {
        var containing = method.ContainingType;
        return containing?.SpecialType == SpecialType.System_String
            && (method.Name == "Format" || method.Name == "Concat");
    }
}
