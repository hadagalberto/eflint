using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using EfLint.Infrastructure;

namespace EfLint.Analyzers;

/// <summary>
/// EFL3001: raw SQL APIs must not be called with SQL built from interpolated or
/// concatenated non-constant values.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RawSqlInjectionAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(EfLintDiagnostics.RawSqlInjection);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(startContext =>
        {
            var symbols = EfSymbols.Create(startContext.Compilation);
            if (!symbols.HasEntityFrameworkCore || !symbols.HasRelational)
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
        if (!symbols.TryGetRawSqlMethod(invocation.TargetMethod, out var method))
        {
            return;
        }

        var sqlArgument = RawSqlHelpers.GetSqlArgument(invocation);
        if (sqlArgument is null || !RawSqlHelpers.ContainsNonConstantValue(sqlArgument.Value))
        {
            return;
        }

        var options = EfLintOptions.Create(context.Options, context.Operation.Syntax.SyntaxTree);
        if (!options.IsRuleEnabled(EfLintDiagnostics.RawSqlInjectionId))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            EfLintDiagnostics.RawSqlInjection,
            sqlArgument.Syntax.GetLocation(),
            method.Name,
            method.SafeName));
    }
}
