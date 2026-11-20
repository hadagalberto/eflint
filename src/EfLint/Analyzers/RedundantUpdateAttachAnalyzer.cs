using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using EfLint.Infrastructure;

namespace EfLint.Analyzers;

/// <summary>
/// EFL2008: Update/Attach called on an entity that is already tracked by the
/// same context that loaded it.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RedundantUpdateAttachAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(EfLintDiagnostics.RedundantUpdateAttach);

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
        if (method.Name is not ("Update" or "Attach")
            || !symbols.IsDbContextType(method.ContainingType)
            || !symbols.IsDbContextType(invocation.Instance?.Type)
            || invocation.Arguments.Length == 0)
        {
            return;
        }

        var entity = RawSqlHelpers.Unwrap(invocation.Arguments[0].Value);
        IOperation? origin = entity;
        if (entity is ILocalReferenceOperation localReference)
        {
            var body = EfOperationHelpers.FindEnclosingFunction(invocation);
            if (body is null)
            {
                return;
            }

            origin = EfOperationHelpers.FindLastValueForSymbol(body, localReference.Local, invocation.Syntax.SpanStart);
        }

        if (origin is null || RawSqlHelpers.Unwrap(origin) is not IInvocationOperation materialization)
        {
            return;
        }

        if (!QueryChain.IsMaterializer(materialization.TargetMethod, symbols))
        {
            return;
        }

        var source = materialization.Instance
            ?? materialization.Arguments.FirstOrDefault()?.Value;
        if (source is null || !QueryChain.IsEfQuery(source, symbols))
        {
            return;
        }

        var originContext = QueryChain.FindContextRoot(source, symbols);
        if (originContext is null || !SameValue(originContext, invocation.Instance!))
        {
            return;
        }

        var options = EfLintOptions.Create(context.Options, invocation.Syntax.SyntaxTree);
        if (!options.IsRuleEnabled(EfLintDiagnostics.RedundantUpdateAttachId))
        {
            return;
        }

        var location = invocation.Syntax is InvocationExpressionSyntax syntax
            ? syntax.Expression switch
            {
                MemberAccessExpressionSyntax member => member.Name.GetLocation(),
                SimpleNameSyntax simple => simple.GetLocation(),
                _ => syntax.GetLocation(),
            }
            : invocation.Syntax.GetLocation();

        context.ReportDiagnostic(Diagnostic.Create(
            EfLintDiagnostics.RedundantUpdateAttach,
            location,
            method.Name));
    }

    private static bool SameValue(IOperation left, IOperation right)
    {
        var a = RawSqlHelpers.Unwrap(left);
        var b = RawSqlHelpers.Unwrap(right);
        if (a is ILocalReferenceOperation la && b is ILocalReferenceOperation lb)
        {
            return SymbolEqualityComparer.Default.Equals(la.Local, lb.Local);
        }

        if (a is IParameterReferenceOperation pa && b is IParameterReferenceOperation pb)
        {
            return SymbolEqualityComparer.Default.Equals(pa.Parameter, pb.Parameter);
        }

        return false;
    }
}
