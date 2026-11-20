using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using EfLint.Infrastructure;

namespace EfLint.Analyzers;

/// <summary>
/// EFL2007: non-sargable string comparisons inside EF query lambdas.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StringComparisonInQueryAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(EfLintDiagnostics.StringComparisonInQuery);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(startContext =>
        {
            var symbols = EfSymbols.Create(startContext.Compilation);
            if (!symbols.HasEntityFrameworkCore || symbols.StringComparison is null)
            {
                return;
            }

            startContext.RegisterOperationAction(
                operationContext => AnalyzeInvocation(operationContext, symbols),
                OperationKind.Invocation);

            startContext.RegisterOperationAction(
                operationContext => AnalyzeBinary(operationContext, symbols),
                OperationKind.Binary);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, EfSymbols symbols)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (method.Name != "Equals"
            || method.ContainingType?.SpecialType != SpecialType.System_String)
        {
            return;
        }

        var hasComparison = invocation.Arguments.Any(argument =>
            SymbolEqualityComparer.Default.Equals(argument.Parameter?.Type, symbols.StringComparison));
        if (!hasComparison || !QueryLambdaHelpers.IsInsideEfQueryLambda(invocation, symbols))
        {
            return;
        }

        Report(context, invocation.Syntax, "string.Equals with StringComparison");
    }

    private static void AnalyzeBinary(OperationAnalysisContext context, EfSymbols symbols)
    {
        var binary = (IBinaryOperation)context.Operation;
        if (binary.OperatorKind is not (BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals)
            || (!IsCaseConversion(binary.LeftOperand) && !IsCaseConversion(binary.RightOperand))
            || !QueryLambdaHelpers.IsInsideEfQueryLambda(binary, symbols))
        {
            return;
        }

        Report(context, binary.Syntax, "ToLower()/ToUpper() comparison");
    }

    private static bool IsCaseConversion(IOperation operation)
    {
        if (RawSqlHelpers.Unwrap(operation) is not IInvocationOperation invocation)
        {
            return false;
        }

        var name = invocation.TargetMethod.Name;
        return invocation.TargetMethod.ContainingType?.SpecialType == SpecialType.System_String
            && name is "ToLower" or "ToUpper" or "ToLowerInvariant" or "ToUpperInvariant"
            && invocation.Arguments.Length == 0;
    }

    private static void Report(OperationAnalysisContext context, SyntaxNode syntax, string comparison)
    {
        var options = EfLintOptions.Create(context.Options, syntax.SyntaxTree);
        if (!options.IsRuleEnabled(EfLintDiagnostics.StringComparisonInQueryId))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            EfLintDiagnostics.StringComparisonInQuery,
            syntax.GetLocation(),
            comparison,
            Suggestion(options.Provider)));
    }

    private static string Suggestion(string provider) => provider switch
    {
        "postgres" => "EF.Functions.ILike or EF.Functions.Collate",
        "sqlite" => "EF.Functions.Collate with the NOCASE collation",
        _ => "EF.Functions.Collate with a case-insensitive collation",
    };
}
