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
        IInvocationOperation? projection = null;
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
                projection = parent;
                break;
            }
        }

        if (projection is null)
        {
            return;
        }

        // A pass-through projection keeps the entity (and therefore the Include) in the
        // result. Removing the Include would change which navigations are populated.
        if (ProjectionPreservesEntity(projection))
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

    /// <summary>
    /// True when the projection lambda yields the included entity itself, either directly or
    /// inside an anonymous/object initializer. Member accesses such as b.Id do not count.
    /// </summary>
    private static bool ProjectionPreservesEntity(IInvocationOperation projection)
    {
        foreach (var argument in projection.Arguments)
        {
            if (RawSqlHelpers.Unwrap(argument.Value) is IAnonymousFunctionOperation lambda
                && lambda.Syntax is LambdaExpressionSyntax lambdaSyntax
                && TryGetParameterName(lambdaSyntax, out var parameterName)
                && lambdaSyntax.Body is ExpressionSyntax body
                && ExpressionPreservesEntity(body, parameterName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetParameterName(LambdaExpressionSyntax lambda, out string name)
    {
        switch (lambda)
        {
            case SimpleLambdaExpressionSyntax simple:
                name = simple.Parameter.Identifier.ValueText;
                return true;

            case ParenthesizedLambdaExpressionSyntax parenthesized when parenthesized.ParameterList.Parameters.Count == 1:
                name = parenthesized.ParameterList.Parameters[0].Identifier.ValueText;
                return true;

            default:
                name = string.Empty;
                return false;
        }
    }

    private static bool ExpressionPreservesEntity(ExpressionSyntax expression, string parameterName)
    {
        expression = expression switch
        {
            ParenthesizedExpressionSyntax parenthesized => parenthesized.Expression,
            CastExpressionSyntax cast => cast.Expression,
            _ => expression,
        };

        if (expression is IdentifierNameSyntax identifier
            && identifier.Identifier.ValueText == parameterName)
        {
            return true;
        }

        if (expression is AnonymousObjectCreationExpressionSyntax anonymous)
        {
            foreach (var initializer in anonymous.Initializers)
            {
                if (ExpressionPreservesEntity(initializer.Expression, parameterName))
                {
                    return true;
                }
            }
        }

        if (expression is ObjectCreationExpressionSyntax { Initializer: not null } objectCreation)
        {
            foreach (var initializer in objectCreation.Initializer.Expressions)
            {
                if (initializer is AssignmentExpressionSyntax assignment
                    && ExpressionPreservesEntity(assignment.Right, parameterName))
                {
                    return true;
                }
            }
        }

        return false;
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
