using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using EfLint.Infrastructure;

namespace EfLint.Analyzers;

/// <summary>
/// EFL3002: an interpolated SQL string assigned to a <c>string</c> local is
/// evaluated before EF Core can bind its values. Data flow is followed within
/// the containing method or lambda.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FlattenedSqlInterpolationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(EfLintDiagnostics.FlattenedSqlInterpolation);

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
        if (sqlArgument is null)
        {
            return;
        }

        if (RawSqlHelpers.Unwrap(sqlArgument.Value) is not ILocalReferenceOperation localReference
            || localReference.Local.Type?.SpecialType != SpecialType.System_String)
        {
            return;
        }

        var body = FindEnclosingBody(invocation);
        var origin = body is null
            ? null
            : FindLastValue(body, localReference.Local, invocation.Syntax.SpanStart);

        if (origin is null || !RawSqlHelpers.ContainsNonConstantValue(origin))
        {
            return;
        }

        var options = EfLintOptions.Create(context.Options, context.Operation.Syntax.SyntaxTree);
        if (!options.IsRuleEnabled(EfLintDiagnostics.FlattenedSqlInterpolationId))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            EfLintDiagnostics.FlattenedSqlInterpolation,
            sqlArgument.Syntax.GetLocation(),
            method.Name,
            method.SafeName));
    }

    private static IOperation? FindEnclosingBody(IOperation operation)
    {
        var current = operation;
        while (current.Parent is not null)
        {
            current = current.Parent;
            if (current.Kind == OperationKind.MethodBody || current.Kind == OperationKind.AnonymousFunction)
            {
                return current;
            }
        }

        return null;
    }

    private static IOperation? FindLastValue(IOperation body, ILocalSymbol local, int usePosition)
    {
        IOperation? best = null;
        var bestPosition = -1;

        foreach (var node in body.DescendantsAndSelf())
        {
            IOperation? value = null;
            switch (node)
            {
                case IVariableDeclaratorOperation declarator
                    when SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) && declarator.Initializer is not null:
                    value = declarator.Initializer.Value;
                    break;

                case ISimpleAssignmentOperation assignment
                    when assignment.Target is ILocalReferenceOperation target
                        && SymbolEqualityComparer.Default.Equals(target.Local, local):
                    value = assignment.Value;
                    break;

                case ICompoundAssignmentOperation compound
                    when compound.Target is ILocalReferenceOperation compoundTarget
                        && SymbolEqualityComparer.Default.Equals(compoundTarget.Local, local):
                    value = compound.Value;
                    break;
            }

            var position = node.Syntax.SpanStart;
            if (value is not null && position <= usePosition && position > bestPosition)
            {
                best = value;
                bestPosition = position;
            }
        }

        return best;
    }
}
