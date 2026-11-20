using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using EfLint.Infrastructure;

namespace EfLint.Analyzers;

/// <summary>
/// EFL2005: DbContext must not be registered with a singleton service lifetime.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SingletonContextAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(EfLintDiagnostics.SingletonContext);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(startContext =>
        {
            var symbols = EfSymbols.Create(startContext.Compilation);
            if (!symbols.HasEntityFrameworkCore || !symbols.HasDependencyInjection)
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
        var containing = method.ContainingType;

        string? contextType = null;

        if (method.Name == "AddSingleton"
            && SymbolEqualityComparer.Default.Equals(containing, symbols.ServiceCollectionServiceExtensions)
            && method.TypeArguments.Length > 0)
        {
            if (!symbols.IsDbContextType(method.TypeArguments[0]))
            {
                return;
            }

            contextType = method.TypeArguments[0].Name;
        }
        else if (method.Name == "AddDbContext"
            && SymbolEqualityComparer.Default.Equals(containing, symbols.EntityFrameworkServiceCollectionExtensions))
        {
            var singletonArgument = invocation.Arguments.FirstOrDefault(argument =>
                SymbolEqualityComparer.Default.Equals(argument.Parameter?.Type, symbols.ServiceLifetime)
                && symbols.IsSingletonLifetime(argument.Value));
            if (singletonArgument is null)
            {
                return;
            }

            contextType = method.TypeArguments.Length > 0
                ? method.TypeArguments[0].Name
                : GetContextTypeFromArguments(invocation, symbols);
        }
        else
        {
            return;
        }

        var options = EfLintOptions.Create(context.Options, invocation.Syntax.SyntaxTree);
        if (!options.IsRuleEnabled(EfLintDiagnostics.SingletonContextId))
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
            EfLintDiagnostics.SingletonContext,
            location,
            contextType ?? "DbContext"));
    }

    private static string? GetContextTypeFromArguments(IInvocationOperation invocation, EfSymbols symbols)
    {
        foreach (var argument in invocation.Arguments)
        {
            var value = RawSqlHelpers.Unwrap(argument.Value);
            if (value is ITypeOfOperation typeOf && symbols.IsDbContextType(typeOf.TypeOperand))
            {
                return typeOf.TypeOperand.Name;
            }
        }

        return null;
    }
}
