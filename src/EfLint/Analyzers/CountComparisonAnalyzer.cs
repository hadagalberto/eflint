using System.Collections.Generic;
using System.Collections.Immutable;
using EfLint.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EfLint.Analyzers;

/// <summary>
/// EFL1002: Count() compared against zero or one is cheaper expressed as Any().
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CountComparisonAnalyzer : DiagnosticAnalyzer
{
    internal const string ReplacementMethodProperty = "ReplacementMethod";

    private static readonly IReadOnlyDictionary<string, string> ReplacementNames =
        new Dictionary<string, string>
        {
            ["Count"] = "Any",
            ["CountAsync"] = "AnyAsync",
            ["LongCount"] = "Any",
            ["LongCountAsync"] = "AnyAsync",
        };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(EfLintDiagnostics.CountComparison);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(startContext =>
        {
            var symbols = EfSymbols.Create(startContext.Compilation);
            if (!symbols.HasEntityFrameworkCore)
            {
                return;
            }

            startContext.RegisterOperationAction(
                operationContext => AnalyzeInvocation(operationContext, symbols),
                OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, EfSymbols symbols)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!ReplacementNames.TryGetValue(invocation.TargetMethod.Name, out var replacementName)
            || !QueryChain.IsMaterializer(invocation.TargetMethod, symbols)
            || !IsOnEfQuery(invocation, symbols)
            || !IsExistenceComparison(invocation))
        {
            return;
        }

        var options = EfLintOptions.Create(context.Options, invocation.Syntax.SyntaxTree);
        if (!options.IsRuleEnabled(EfLintDiagnostics.CountComparisonId))
        {
            return;
        }

        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(ReplacementMethodProperty, replacementName);

        context.ReportDiagnostic(Diagnostic.Create(
            EfLintDiagnostics.CountComparison,
            GetNameLocation(invocation),
            properties,
            invocation.TargetMethod.Name));
    }

    private static bool IsOnEfQuery(IInvocationOperation invocation, EfSymbols symbols)
    {
        if (invocation.Instance is not null && QueryChain.IsEfQuery(invocation.Instance, symbols))
        {
            return true;
        }

        foreach (var argument in invocation.Arguments)
        {
            if (QueryChain.IsEfQuery(argument.Value, symbols))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True only for the comparisons logically equivalent to "a row exists" that
    /// have a one-to-one replacement with Any(): &gt; 0, != 0, &gt;= 1 and their
    /// reversed forms 0 &lt; x and 1 &lt;= x. Other comparisons (== 0, &lt; 1,
    /// &gt; 1, &gt;= 2, ...) are deliberately left alone.
    /// </summary>
    private static bool IsExistenceComparison(IInvocationOperation invocation)
    {
        IOperation current = invocation;
        // An awaited CountAsync is wrapped in IAwaitOperation; climb it so the
        // existence comparison sees the same binary shape as the sync Count() case.
        while (current.Parent is IConversionOperation or IParenthesizedOperation or IAwaitOperation)
        {
            current = current.Parent!;
        }

        if (current.Parent is not IBinaryOperation binary)
        {
            return false;
        }

        var leftIsCount = ReferenceEquals(binary.LeftOperand, current);
        var rightIsCount = ReferenceEquals(binary.RightOperand, current);
        if (leftIsCount == rightIsCount)
        {
            return false;
        }

        var constantOperand = RawSqlHelpers.Unwrap(leftIsCount ? binary.RightOperand : binary.LeftOperand);
        long? constant = constantOperand.ConstantValue.Value switch
        {
            int value => (long)value,
            long value => value,
            _ => null,
        };

        if (constant is null)
        {
            return false;
        }

        var op = binary.OperatorKind;
        return leftIsCount
            ? (op == BinaryOperatorKind.GreaterThan && constant == 0)
                || (op == BinaryOperatorKind.NotEquals && constant == 0)
                || (op == BinaryOperatorKind.GreaterThanOrEqual && constant == 1)
            : (op == BinaryOperatorKind.LessThan && constant == 0)
                || (op == BinaryOperatorKind.LessThanOrEqual && constant == 1);
    }

    private static Location GetNameLocation(IInvocationOperation invocation)
    {
        if (invocation.Syntax is InvocationExpressionSyntax syntax)
        {
            return syntax.Expression switch
            {
                MemberAccessExpressionSyntax member => member.Name.GetLocation(),
                SimpleNameSyntax simple => simple.GetLocation(),
                _ => syntax.GetLocation(),
            };
        }

        return invocation.Syntax.GetLocation();
    }
}
