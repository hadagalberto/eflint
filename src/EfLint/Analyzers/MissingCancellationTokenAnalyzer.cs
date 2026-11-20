using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using EfLint.Infrastructure;

namespace EfLint.Analyzers;

/// <summary>
/// EFL2002: an asynchronous EF Core operation must receive a CancellationToken
/// when one is available in the enclosing method or lambda.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingCancellationTokenAnalyzer : DiagnosticAnalyzer
{
    internal const string CancellationTokenProperty = "CancellationToken";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(EfLintDiagnostics.MissingCancellationToken);

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

        var isEfAsync = symbols.IsEfAsyncQueryMethod(method)
            || ((method.Name is "SaveChangesAsync" or "FindAsync")
                && symbols.IsDbContextType(method.ContainingType)
                && EfSymbols.HasCancellationTokenParameter(method));
        if (!isEfAsync)
        {
            return;
        }

        var alreadyPassed = invocation.Arguments.Any(argument =>
            argument.ArgumentKind == ArgumentKind.Explicit
            && (symbols.IsCancellationTokenType(argument.Parameter?.Type)
                || symbols.IsCancellationTokenType(argument.Value.Type)));
        if (alreadyPassed)
        {
            return;
        }

        var token = EfOperationHelpers.FindCancellationTokenInScope(invocation, symbols);
        if (token is null)
        {
            return;
        }

        var options = EfLintOptions.Create(context.Options, invocation.Syntax.SyntaxTree);
        if (!options.IsRuleEnabled(EfLintDiagnostics.MissingCancellationTokenId))
        {
            return;
        }

        var properties = ImmutableDictionary<string, string?>.Empty.Add(CancellationTokenProperty, token.Name);
        context.ReportDiagnostic(Diagnostic.Create(
            EfLintDiagnostics.MissingCancellationToken,
            GetNameLocation(invocation),
            properties,
            method.Name,
            token.Name));
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
