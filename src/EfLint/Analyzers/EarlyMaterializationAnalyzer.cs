using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using EfLint.Infrastructure;

namespace EfLint.Analyzers;

/// <summary>
/// EFL1001: ToList/ToArray/AsEnumerable in the middle of an EF query loads every
/// row into memory before the remaining LINQ operators are applied.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EarlyMaterializationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(EfLintDiagnostics.EarlyMaterialization);

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
        if (name is not ("ToList" or "ToArray" or "AsEnumerable")
            || !IsLinqOrEfMethod(invocation.TargetMethod, symbols))
        {
            return;
        }

        var source = GetQuerySource(invocation, symbols);
        if (source is null || !QueryChain.IsEfQuery(source, symbols))
        {
            return;
        }

        // The materializer must be the direct receiver of a later query operator.
        if (invocation.Syntax is not InvocationExpressionSyntax syntax
            || syntax.Parent is not MemberAccessExpressionSyntax member
            || member.Expression != syntax
            || member.Parent is not InvocationExpressionSyntax outer
            || outer.Expression != member)
        {
            return;
        }

        var semanticModel = context.Operation.SemanticModel;
        if (semanticModel is null
            || semanticModel.GetSymbolInfo(outer, context.CancellationToken).Symbol is not IMethodSymbol outerMethod
            || outerMethod.Name is not ("Where" or "Select" or "SelectMany" or "OrderBy" or "OrderByDescending" or "Skip" or "Take")
            || !IsQueryOperator(outerMethod, symbols, outerMethod.Name))
        {
            return;
        }

        var options = EfLintOptions.Create(context.Options, syntax.SyntaxTree);
        if (!options.IsRuleEnabled(EfLintDiagnostics.EarlyMaterializationId))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            EfLintDiagnostics.EarlyMaterialization,
            GetNameLocation(syntax),
            name,
            outerMethod.Name));
    }

    private static IOperation? GetQuerySource(IInvocationOperation invocation, EfSymbols symbols)
    {
        if (invocation.Instance is not null)
        {
            return invocation.Instance;
        }

        foreach (var argument in invocation.Arguments)
        {
            // Enumerable.ToList takes IEnumerable<T>, so neither the parameter type
            // nor the conversion around the receiver can identify the EF query.
            var value = RawSqlHelpers.Unwrap(argument.Value);
            if (symbols.IsQueryableType(value.Type) || symbols.IsDbSetType(value.Type))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// QueryChain only treats Queryable and EF operators as query operators; after a
    /// materializer the receiver is IEnumerable, so Enumerable operators count too.
    /// </summary>
    private static bool IsQueryOperator(IMethodSymbol method, EfSymbols symbols, string name)
    {
        return QueryChain.IsQueryOperatorNamed(method, symbols, name)
            || (method.Name == name && IsEnumerableMethod(method));
    }

    private static bool IsLinqOrEfMethod(IMethodSymbol method, EfSymbols symbols)
    {
        var containing = method.ContainingType;
        if (SymbolEqualityComparer.Default.Equals(containing, symbols.EntityFrameworkQueryableExtensions)
            || SymbolEqualityComparer.Default.Equals(containing, symbols.RelationalQueryableExtensions))
        {
            return true;
        }

        return IsEnumerableMethod(method) || IsQueryableMethod(method);
    }

    private static bool IsEnumerableMethod(IMethodSymbol method)
    {
        var containing = method.ContainingType;
        var ns = containing?.ContainingNamespace;
        return containing?.Name == "Enumerable"
            && ns?.Name == "Linq"
            && ns.ContainingNamespace?.Name == "System";
    }

    private static bool IsQueryableMethod(IMethodSymbol method)
    {
        var containing = method.ContainingType;
        var ns = containing?.ContainingNamespace;
        return containing?.Name == "Queryable"
            && ns?.Name == "Linq"
            && ns.ContainingNamespace?.Name == "System";
    }

    private static Location GetNameLocation(InvocationExpressionSyntax syntax)
    {
        return syntax.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.GetLocation(),
            SimpleNameSyntax simple => simple.GetLocation(),
            _ => syntax.GetLocation(),
        };
    }
}
