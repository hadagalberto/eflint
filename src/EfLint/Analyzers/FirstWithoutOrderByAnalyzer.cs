using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using EfLint.Infrastructure;

namespace EfLint.Analyzers;

/// <summary>
/// EFL1005: First/FirstOrDefault without an explicit ordering selects a non-deterministic row.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FirstWithoutOrderByAnalyzer : DiagnosticAnalyzer
{
    private const int MaxChainDepth = 64;

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(EfLintDiagnostics.FirstWithoutOrderBy);

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
        var name = invocation.TargetMethod.Name;
        if (name != "First" && name != "FirstOrDefault")
        {
            return;
        }

        // First/FirstOrDefault materialize a query: the EF query is the receiver/source,
        // not the invocation itself.
        var source = GetQuerySource(invocation, symbols);
        if (source is null || !QueryChain.IsEfQuery(source, symbols))
        {
            return;
        }

        if (HasOrdering(source, symbols))
        {
            return;
        }

        var options = EfLintOptions.Create(context.Options, invocation.Syntax.SyntaxTree);
        if (!options.IsRuleEnabled(EfLintDiagnostics.FirstWithoutOrderById))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            EfLintDiagnostics.FirstWithoutOrderBy,
            GetNameLocation(invocation),
            name));
    }

    /// <summary>Walks the query chain below the materializer looking for an ordering operator.</summary>
    private static bool HasOrdering(IOperation source, EfSymbols symbols)
    {
        var current = RawSqlHelpers.Unwrap(source);
        for (var depth = 0; depth < MaxChainDepth && current is not null; depth++)
        {
            if (current is IInvocationOperation invocation)
            {
                if (IsOrderingOperator(invocation.TargetMethod, symbols))
                {
                    return true;
                }

                var next = GetQuerySource(invocation, symbols);
                if (next is null)
                {
                    return false;
                }

                current = RawSqlHelpers.Unwrap(next);
                continue;
            }

            if (current is IMemberReferenceOperation member && member.Instance is not null)
            {
                current = RawSqlHelpers.Unwrap(member.Instance);
                continue;
            }

            return false;
        }

        return false;
    }

    private static bool IsOrderingOperator(IMethodSymbol method, EfSymbols symbols)
    {
        return QueryChain.IsQueryOperatorNamed(method, symbols, "OrderBy")
            || QueryChain.IsQueryOperatorNamed(method, symbols, "OrderByDescending")
            || QueryChain.IsQueryOperatorNamed(method, symbols, "ThenBy")
            || QueryChain.IsQueryOperatorNamed(method, symbols, "ThenByDescending");
    }

    /// <summary>Returns the queryable receiver of an invocation (instance or LINQ source argument).</summary>
    private static IOperation? GetQuerySource(IInvocationOperation invocation, EfSymbols symbols)
    {
        if (invocation.Instance is not null && symbols.IsQueryableType(invocation.Instance.Type))
        {
            return invocation.Instance;
        }

        foreach (var argument in invocation.Arguments)
        {
            if (symbols.IsQueryableType(argument.Value.Type)
                || symbols.IsQueryableType(argument.Parameter?.Type))
            {
                return argument.Value;
            }
        }

        return null;
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
