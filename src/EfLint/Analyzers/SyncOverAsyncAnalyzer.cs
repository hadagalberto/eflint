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
/// EFL2001: synchronous EF Core queries, SaveChanges and Find called inside an
/// async method when an asynchronous overload exists.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SyncOverAsyncAnalyzer : DiagnosticAnalyzer
{
    internal const string AsyncNameProperty = "AsyncName";
    internal const string CancellationTokenProperty = "CancellationToken";
    internal const string IsFindProperty = "IsFind";
    internal const string EntityTypeProperty = "EntityType";

    private static readonly IReadOnlyDictionary<string, string> AsyncNames = new Dictionary<string, string>
    {
        ["ToList"] = "ToListAsync",
        ["ToArray"] = "ToArrayAsync",
        ["ToDictionary"] = "ToDictionaryAsync",
        ["First"] = "FirstAsync",
        ["FirstOrDefault"] = "FirstOrDefaultAsync",
        ["Single"] = "SingleAsync",
        ["SingleOrDefault"] = "SingleOrDefaultAsync",
        ["Last"] = "LastAsync",
        ["LastOrDefault"] = "LastOrDefaultAsync",
        ["Count"] = "CountAsync",
        ["LongCount"] = "LongCountAsync",
        ["Any"] = "AnyAsync",
        ["All"] = "AllAsync",
        ["Sum"] = "SumAsync",
        ["Min"] = "MinAsync",
        ["Max"] = "MaxAsync",
        ["Average"] = "AverageAsync",
        ["ElementAt"] = "ElementAtAsync",
        ["ElementAtOrDefault"] = "ElementAtOrDefaultAsync",
        ["Contains"] = "ContainsAsync",
        ["ExecuteDelete"] = "ExecuteDeleteAsync",
        ["ExecuteUpdate"] = "ExecuteUpdateAsync",
        ["SaveChanges"] = "SaveChangesAsync",
        ["Find"] = "FindAsync",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(EfLintDiagnostics.SyncOverAsync);

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
        if (!AsyncNames.TryGetValue(method.Name, out var asyncName))
        {
            return;
        }

        if (!EfOperationHelpers.IsInsideAsyncFunction(invocation))
        {
            return;
        }

        var isContextMethod = method.Name is "SaveChanges" or "Find"
            && symbols.IsDbContextType(method.ContainingType);
        if (isContextMethod)
        {
            if (!symbols.IsDbContextType(invocation.Instance?.Type))
            {
                return;
            }
        }
        else
        {
            if (!QueryChain.IsMaterializer(method, symbols))
            {
                return;
            }

            var source = invocation.Instance ?? invocation.Arguments.FirstOrDefault()?.Value;
            if (source is null || !QueryChain.IsEfQuery(source, symbols))
            {
                return;
            }
        }

        var options = EfLintOptions.Create(context.Options, invocation.Syntax.SyntaxTree);
        if (!options.IsRuleEnabled(EfLintDiagnostics.SyncOverAsyncId))
        {
            return;
        }

        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(AsyncNameProperty, asyncName)
            .Add(IsFindProperty, method.Name == "Find" ? "true" : "false");

        if (method.Name == "Find" && method.TypeArguments.Length > 0)
        {
            properties = properties.Add(EntityTypeProperty, method.TypeArguments[0].ToDisplayString());
        }

        var token = EfOperationHelpers.FindCancellationTokenInScope(invocation, symbols);
        if (token is not null)
        {
            properties = properties.Add(CancellationTokenProperty, token.Name);
        }

        context.ReportDiagnostic(Diagnostic.Create(
            EfLintDiagnostics.SyncOverAsync,
            GetNameLocation(invocation),
            properties,
            method.Name,
            asyncName));
    }

    internal static Location GetNameLocation(IInvocationOperation invocation)
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
