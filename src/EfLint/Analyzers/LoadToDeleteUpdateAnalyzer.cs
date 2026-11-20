using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using EfLint.Infrastructure;

namespace EfLint.Analyzers;

/// <summary>
/// EFL1008: an entity is loaded from an EF query only to be removed, updated or
/// have one of its properties assigned. ExecuteDelete/ExecuteUpdate (EF Core 7+)
/// express the same operation in a single statement.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LoadToDeleteUpdateAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(EfLintDiagnostics.LoadToDeleteUpdate);

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

            startContext.RegisterOperationAction(
                operationContext => AnalyzePropertyAssignment(operationContext, symbols),
                OperationKind.SimpleAssignment);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, EfSymbols symbols)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (method.Name is not ("Remove" or "RemoveRange" or "Update" or "UpdateRange"))
        {
            return;
        }

        var instance = invocation.Instance is null ? null : RawSqlHelpers.Unwrap(invocation.Instance);
        if (instance is null || !symbols.IsDbSetType(instance.Type) || invocation.Arguments.Length == 0)
        {
            return;
        }

        if (RawSqlHelpers.Unwrap(invocation.Arguments[0].Value) is not ILocalReferenceOperation local)
        {
            return;
        }

        if (!IsLoadedFromEfQuery(local.Local, invocation, symbols)
            || !IsRuleEnabled(context, invocation.Syntax))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            EfLintDiagnostics.LoadToDeleteUpdate,
            GetMemberNameLocation(invocation.Syntax),
            method.Name));
    }

    private static void AnalyzePropertyAssignment(OperationAnalysisContext context, EfSymbols symbols)
    {
        var assignment = (ISimpleAssignmentOperation)context.Operation;
        if (RawSqlHelpers.Unwrap(assignment.Target) is not IPropertyReferenceOperation property
            || property.Instance is null
            || RawSqlHelpers.Unwrap(property.Instance) is not ILocalReferenceOperation local)
        {
            return;
        }

        if (!IsLoadedFromEfQuery(local.Local, assignment, symbols)
            || !IsRuleEnabled(context, assignment.Syntax))
        {
            return;
        }

        var location = property.Syntax is MemberAccessExpressionSyntax member
            ? member.Name.GetLocation()
            : property.Syntax.GetLocation();

        context.ReportDiagnostic(Diagnostic.Create(
            EfLintDiagnostics.LoadToDeleteUpdate,
            location,
            "Update"));
    }

    /// <summary>
    /// True when the last value assigned to <paramref name="local"/> before its use
    /// is a materializer (or Find) over a query rooted in EF Core.
    /// </summary>
    private static bool IsLoadedFromEfQuery(ILocalSymbol local, IOperation use, EfSymbols symbols)
    {
        var body = EfOperationHelpers.FindEnclosingFunction(use);
        if (body is null)
        {
            return false;
        }

        var origin = EfOperationHelpers.FindLastValueForSymbol(body, local, use.Syntax.SpanStart);
        if (origin is null || RawSqlHelpers.Unwrap(origin) is not IInvocationOperation materialization)
        {
            return false;
        }

        var method = materialization.TargetMethod;
        if (method.Name == "Find")
        {
            if (!symbols.IsDbSetType(materialization.Instance?.Type)
                && !symbols.IsDbContextType(materialization.Instance?.Type))
            {
                return false;
            }
        }
        else if (!QueryChain.IsMaterializer(method, symbols))
        {
            return false;
        }

        var source = materialization.Instance ?? materialization.Arguments.FirstOrDefault()?.Value;
        return source is not null && QueryChain.IsEfQuery(source, symbols);
    }

    private static bool IsRuleEnabled(OperationAnalysisContext context, SyntaxNode syntax)
    {
        return EfLintOptions.Create(context.Options, syntax.SyntaxTree)
            .IsRuleEnabled(EfLintDiagnostics.LoadToDeleteUpdateId);
    }

    private static Location GetMemberNameLocation(SyntaxNode syntax)
    {
        if (syntax is InvocationExpressionSyntax invocation)
        {
            return invocation.Expression switch
            {
                MemberAccessExpressionSyntax member => member.Name.GetLocation(),
                SimpleNameSyntax simple => simple.GetLocation(),
                _ => syntax.GetLocation(),
            };
        }

        return syntax.GetLocation();
    }
}
