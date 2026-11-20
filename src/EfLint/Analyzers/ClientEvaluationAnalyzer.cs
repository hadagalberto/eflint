using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using EfLint.Infrastructure;

namespace EfLint.Analyzers;

/// <summary>
/// EFL2003: user-defined methods called inside query lambdas cannot be
/// translated to SQL by the EF Core provider.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ClientEvaluationAnalyzer : DiagnosticAnalyzer
{
    private static readonly HashSet<string> LambdaOperatorNames = new()
    {
        "Where", "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending",
        "Select", "SelectMany", "Any", "All", "Count", "LongCount", "First",
        "FirstOrDefault", "Single", "SingleOrDefault", "Last", "LastOrDefault",
        "Sum", "Min", "Max", "Average", "Contains",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(EfLintDiagnostics.ClientEvaluation);

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

        if (!EfOperationHelpers.IsUserDefinedMethod(method)
            || symbols.HasDbFunctionAttribute(method))
        {
            return;
        }

        if (!QueryLambdaHelpers.IsInsideEfQueryLambda(invocation, symbols))
        {
            return;
        }

        // EF Core evaluates the outermost final projection on the client after
        // translating the query, so user methods there are supported.
        if (IsAllowedFinalProjection(invocation, symbols))
        {
            return;
        }

        var options = EfLintOptions.Create(context.Options, invocation.Syntax.SyntaxTree);
        if (!options.IsRuleEnabled(EfLintDiagnostics.ClientEvaluationId))
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
            EfLintDiagnostics.ClientEvaluation,
            location,
            method.Name));
    }

    /// <summary>
    /// True when the invocation lives only in EF Select lambdas and the outermost one
    /// is the final projection of the query (nothing composes another operator after
    /// it). Any enclosing Where/OrderBy/subquery lambda makes client evaluation
    /// non-translatable, so those are still reported.
    /// </summary>
    private static bool IsAllowedFinalProjection(IOperation invocation, EfSymbols symbols)
    {
        var sawNonSelectLambda = false;
        IInvocationOperation? outermostSelect = null;

        IOperation current = invocation;
        while (current.Parent is not null)
        {
            current = current.Parent;
            if (current is not IAnonymousFunctionOperation)
            {
                continue;
            }

            var parent = current.Parent;
            while (parent is IConversionOperation or IParenthesizedOperation)
            {
                parent = parent.Parent!;
            }

            if (parent is not IArgumentOperation argument
                || argument.Parent is not IInvocationOperation owner
                || !LambdaOperatorNames.Contains(owner.TargetMethod.Name))
            {
                continue;
            }

            var source = owner.Instance
                ?? owner.Arguments.FirstOrDefault(a => symbols.IsQueryableType(a.Parameter?.Type))?.Value;
            if (source is null || !QueryChain.IsEfQuery(source, symbols))
            {
                continue;
            }

            if (owner.TargetMethod.Name is "Select" or "SelectMany")
            {
                outermostSelect = owner;
            }
            else
            {
                sawNonSelectLambda = true;
            }
        }

        return !sawNonSelectLambda
            && outermostSelect is not null
            && IsFinalProjection(outermostSelect, symbols);
    }

    private static bool IsFinalProjection(IInvocationOperation select, EfSymbols symbols)
    {
        IOperation current = select;
        while (current.Parent is not null)
        {
            var parent = current.Parent;
            if (parent is IConversionOperation or IParenthesizedOperation)
            {
                current = parent;
                continue;
            }

            // An extension-method receiver is wrapped in an IArgumentOperation.
            var owner = parent switch
            {
                IInvocationOperation invocation => invocation,
                IArgumentOperation argument => argument.Parent as IInvocationOperation,
                _ => null,
            };
            if (owner is null
                || !(ReferenceEquals(owner.Instance, current)
                    || owner.Arguments.Any(argument => ReferenceEquals(argument.Value, current))))
            {
                // Assignment, materialization into a variable, etc. ends the query.
                return true;
            }

            // A further query operator (Where, OrderBy, ...) means the projection is
            // still composed into SQL; a materializer or other consumer ends the query.
            return !QueryChain.IsEfQuery(owner, symbols);
        }

        return true;
    }
}
