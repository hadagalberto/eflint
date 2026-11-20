using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using EfLint.Infrastructure;

namespace EfLint.Analyzers;

/// <summary>
/// EFL2004: a single DbContext instance captured by a Parallel loop body or a
/// Task.WhenAll projection is used concurrently, which DbContext does not support.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ContextAcrossThreadsAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(EfLintDiagnostics.ContextAcrossThreads);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(startContext =>
        {
            var symbols = EfSymbols.Create(startContext.Compilation);
            if (!symbols.HasEntityFrameworkCore || symbols.Parallel is null || symbols.Task is null)
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
        var method = invocation.TargetMethod;
        var containing = method.ContainingType;

        string kind;
        if (SymbolEqualityComparer.Default.Equals(containing, symbols.Parallel)
            && method.Name is "ForEach" or "For" or "Invoke")
        {
            kind = "Parallel." + method.Name;
        }
        else if (SymbolEqualityComparer.Default.Equals(containing, symbols.Task)
            && method.Name == "WhenAll")
        {
            kind = "Task.WhenAll";
        }
        else
        {
            return;
        }

        var options = EfLintOptions.Create(context.Options, invocation.Syntax.SyntaxTree);
        if (!options.IsRuleEnabled(EfLintDiagnostics.ContextAcrossThreadsId))
        {
            return;
        }

        foreach (var argument in invocation.Arguments)
        {
            foreach (var lambda in argument.Value.DescendantsAndSelf().OfType<IAnonymousFunctionOperation>())
            {
                var captured = FindCapturedContext(lambda, symbols);
                if (captured is null)
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    EfLintDiagnostics.ContextAcrossThreads,
                    captured.Syntax.GetLocation(),
                    kind));
            }
        }
    }

    private static IOperation? FindCapturedContext(IAnonymousFunctionOperation lambda, EfSymbols symbols)
    {
        foreach (var node in lambda.Descendants())
        {
            ISymbol? referenced = node switch
            {
                ILocalReferenceOperation local => local.Local,
                IParameterReferenceOperation parameter => parameter.Parameter,
                IFieldReferenceOperation field => field.Field,
                _ => null,
            };

            if (referenced is not null
                && symbols.IsDbContextType(node.Type)
                && EfOperationHelpers.IsCaptured(lambda, referenced))
            {
                return node;
            }
        }

        return null;
    }
}
