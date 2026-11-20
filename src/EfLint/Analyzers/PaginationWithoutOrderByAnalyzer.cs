using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using EfLint.Infrastructure;

namespace EfLint.Analyzers;

/// <summary>
/// EFL1006: Skip/Take without an explicit ordering returns non-deterministic pages.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PaginationWithoutOrderByAnalyzer : DiagnosticAnalyzer
{
    private const int MaxChainDepth = 64;

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(EfLintDiagnostics.PaginationWithoutOrderBy);

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
        var method = invocation.TargetMethod;

        var isSkip = QueryChain.IsQueryOperatorNamed(method, symbols, "Skip");
        var isTake = QueryChain.IsQueryOperatorNamed(method, symbols, "Take");
        if (!isSkip && !isTake)
        {
            return;
        }

        var source = GetSource(invocation, symbols);
        if (source is null || !QueryChain.IsEfQuery(source, symbols))
        {
            return;
        }

        if (HasOrderingInChain(invocation, symbols))
        {
            return;
        }

        var options = EfLintOptions.Create(context.Options, invocation.Syntax.SyntaxTree);
        if (!options.IsRuleEnabled(EfLintDiagnostics.PaginationWithoutOrderById))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            EfLintDiagnostics.PaginationWithoutOrderBy,
            GetNameLocation(invocation),
            method.Name));
    }

    private static bool HasOrderingInChain(IInvocationOperation invocation, EfSymbols symbols)
    {
        return WalkSourcesForOrderBy(invocation, symbols)
            || WalkParentsForOrderBy(invocation, symbols);
    }

    /// <summary>Walks down through the receiver chain looking for an ordering operator.</summary>
    private static bool WalkSourcesForOrderBy(IOperation operation, EfSymbols symbols)
    {
        var current = RawSqlHelpers.Unwrap(operation);
        for (var depth = 0; depth < MaxChainDepth; depth++)
        {
            // A query staged in a local must be followed to its initializer/assignment
            // before the receiver chain can be inspected for OrderBy.
            var localValue = FollowLocal(current);
            if (localValue is not null)
            {
                current = localValue;
                // The value may be another local; re-run the checks on it.
                if (current is not IInvocationOperation)
                {
                    continue;
                }
            }

            if (current is not IInvocationOperation invocation)
            {
                return false;
            }

            if (IsOrderingOperator(invocation, symbols))
            {
                return true;
            }

            var source = GetSource(invocation, symbols);
            if (source is null)
            {
                return false;
            }

            current = RawSqlHelpers.Unwrap(source);
        }

        return false;
    }

    /// <summary>Walks up through enclosing invocations that consume this query.</summary>
    private static bool WalkParentsForOrderBy(IOperation operation, EfSymbols symbols)
    {
        IOperation current = operation;
        for (var depth = 0; depth < MaxChainDepth && current.Parent is not null; depth++)
        {
            if (current.Parent is not IInvocationOperation parent)
            {
                return false;
            }

            var consumesCurrent = ReferenceEquals(parent.Instance, current)
                || parent.Arguments.Any(argument => ReferenceEquals(argument.Value, current));
            if (!consumesCurrent)
            {
                return false;
            }

            if (IsOrderingOperator(parent, symbols))
            {
                return true;
            }

            current = parent;
        }

        return false;
    }

    /// <summary>
    /// Resolves a local reference to the last value assigned to it before this use,
    /// mirroring QueryChain's local handling so staging a query in a variable does
    /// not hide an existing OrderBy from the pagination check.
    /// </summary>
    private static IOperation? FollowLocal(IOperation operation)
    {
        if (RawSqlHelpers.Unwrap(operation) is not ILocalReferenceOperation local)
        {
            return null;
        }

        var body = EfOperationHelpers.FindEnclosingFunction(local);
        if (body is null)
        {
            return null;
        }

        var value = EfOperationHelpers.FindLastValueForSymbol(body, local.Local, local.Syntax.SpanStart);
        return value is null ? null : RawSqlHelpers.Unwrap(value);
    }

    private static IOperation? GetSource(IInvocationOperation invocation, EfSymbols symbols)
    {
        return invocation.Instance
            ?? invocation.Arguments.FirstOrDefault(argument => symbols.IsQueryableType(argument.Parameter?.Type))?.Value;
    }

    private static bool IsOrderingOperator(IInvocationOperation invocation, EfSymbols symbols)
    {
        return QueryChain.IsQueryOperatorNamed(invocation.TargetMethod, symbols, "OrderBy")
            || QueryChain.IsQueryOperatorNamed(invocation.TargetMethod, symbols, "OrderByDescending")
            || QueryChain.IsQueryOperatorNamed(invocation.TargetMethod, symbols, "ThenBy")
            || QueryChain.IsQueryOperatorNamed(invocation.TargetMethod, symbols, "ThenByDescending");
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
