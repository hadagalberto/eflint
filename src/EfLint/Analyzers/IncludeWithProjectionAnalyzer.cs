using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using EfLint.Infrastructure;

namespace EfLint.Analyzers;

/// <summary>
/// EFL2006: Include is silently ignored when a query is projected with Select.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IncludeWithProjectionAnalyzer : DiagnosticAnalyzer
{
    internal const string FixableProperty = "Fixable";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(EfLintDiagnostics.IncludeWithProjection);

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
        if (!QueryChain.IsQueryOperatorNamed(invocation.TargetMethod, symbols, "Include"))
        {
            return;
        }

        var hasThenInclude = false;
        var hasProjection = false;
        IOperation current = invocation;
        while (current.Parent is not null)
        {
            current = current.Parent;
            if (current is not IInvocationOperation parent)
            {
                continue;
            }

            if (QueryChain.IsQueryOperatorNamed(parent.TargetMethod, symbols, "ThenInclude"))
            {
                hasThenInclude = true;
            }

            if (QueryChain.IsQueryOperatorNamed(parent.TargetMethod, symbols, "Select")
                || QueryChain.IsQueryOperatorNamed(parent.TargetMethod, symbols, "SelectMany"))
            {
                hasProjection = true;
                break;
            }
        }

        if (!hasProjection)
        {
            return;
        }

        var options = EfLintOptions.Create(context.Options, invocation.Syntax.SyntaxTree);
        if (!options.IsRuleEnabled(EfLintDiagnostics.IncludeWithProjectionId))
        {
            return;
        }

        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(FixableProperty, hasThenInclude ? "false" : "true");
        context.ReportDiagnostic(Diagnostic.Create(
            EfLintDiagnostics.IncludeWithProjection,
            GetNameLocation(invocation),
            properties));
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
