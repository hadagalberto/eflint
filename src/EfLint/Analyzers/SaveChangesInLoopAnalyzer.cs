using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using EfLint.Infrastructure;

namespace EfLint.Analyzers;

/// <summary>
/// EFL1007: SaveChanges/SaveChangesAsync called inside a loop opens one transaction
/// and one round trip per iteration.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SaveChangesInLoopAnalyzer : DiagnosticAnalyzer
{
    internal const string FixableProperty = "Fixable";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(EfLintDiagnostics.SaveChangesInLoop);

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
        if (method.Name is not ("SaveChanges" or "SaveChangesAsync")
            || !symbols.IsDbContextType(method.ContainingType)
            || !symbols.IsDbContextType(invocation.Instance?.Type))
        {
            return;
        }

        if (!IsInsideLoop(invocation))
        {
            return;
        }

        var options = EfLintOptions.Create(context.Options, invocation.Syntax.SyntaxTree);
        if (!options.IsRuleEnabled(EfLintDiagnostics.SaveChangesInLoopId))
        {
            return;
        }

        var fixable = invocation.Syntax is InvocationExpressionSyntax syntax && IsFixable(syntax);
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(FixableProperty, fixable ? "true" : "false");

        context.ReportDiagnostic(Diagnostic.Create(
            EfLintDiagnostics.SaveChangesInLoop,
            GetNameLocation(invocation),
            properties,
            method.Name));
    }

    private static bool IsInsideLoop(IOperation operation)
    {
        for (IOperation? current = operation.Parent; current is not null; current = current.Parent)
        {
            if (current is IForLoopOperation or IForEachLoopOperation or IWhileLoopOperation)
            {
                return true;
            }
        }

        foreach (var ancestor in operation.Syntax.Ancestors())
        {
            if (ancestor is ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFixable(InvocationExpressionSyntax invocation)
    {
        // Only a standalone statement directly in the loop body can be relocated safely.
        if (invocation.Parent is not ExpressionStatementSyntax saveStatement)
        {
            return false;
        }

        var loop = FindEnclosingLoopStatement(invocation);
        if (loop is null)
        {
            return false;
        }

        var body = GetLoopBody(loop);
        if (body is null)
        {
            return false;
        }

        if (body is BlockSyntax block)
        {
            if (!ReferenceEquals(saveStatement.Parent, block))
            {
                return false;
            }

            // Moving the save after the loop is wrong when a later statement consumes a
            // database-generated key (or the save result) produced by this call.
            return !HasGeneratedKeyDependencyAfter(block, saveStatement);
        }

        return ReferenceEquals(body, saveStatement);
    }

    private static bool HasGeneratedKeyDependencyAfter(BlockSyntax block, StatementSyntax saveStatement)
    {
        var saveEnd = saveStatement.Span.End;
        foreach (var node in block.DescendantNodes())
        {
            if (node.SpanStart <= saveEnd)
            {
                continue;
            }

            if (node is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText == "Id")
            {
                return true;
            }

            // An assignment from the SaveChanges result cannot be moved either; such a
            // call is not a direct expression statement and was rejected above already.
        }

        return false;
    }

    internal static StatementSyntax? FindEnclosingLoopStatement(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax)
            {
                return (StatementSyntax)ancestor;
            }
        }

        return null;
    }

    internal static StatementSyntax? GetLoopBody(StatementSyntax loop)
    {
        return loop switch
        {
            ForStatementSyntax forStatement => forStatement.Statement,
            ForEachStatementSyntax forEachStatement => forEachStatement.Statement,
            WhileStatementSyntax whileStatement => whileStatement.Statement,
            DoStatementSyntax doStatement => doStatement.Statement,
            _ => null,
        };
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
