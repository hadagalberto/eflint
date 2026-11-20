using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using EfLint.Infrastructure;

namespace EfLint.Analyzers;

/// <summary>
/// EFL1009: a large in-memory collection used with Contains inside an EF query
/// produces a large parameter list or inline SQL, and may fail on providers
/// with parameter limits.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LargeContainsAnalyzer : DiagnosticAnalyzer
{
    private const int ReportThreshold = 3;

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(EfLintDiagnostics.LargeContains);

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

        if (method.Name != "Contains")
        {
            return;
        }

        // string.Contains is translatable and has no value collection to count.
        if (invocation.Instance?.Type?.SpecialType == SpecialType.System_String
            || method.ContainingType?.SpecialType == SpecialType.System_String)
        {
            return;
        }

        var options = EfLintOptions.Create(context.Options, invocation.Syntax.SyntaxTree);
        if (!options.IsRuleEnabled(EfLintDiagnostics.LargeContainsId))
        {
            return;
        }

        if (!IsInsideEfQueryLambda(invocation, symbols))
        {
            return;
        }

        var collection = GetCollection(invocation);
        if (collection is null
            || collection.Type?.SpecialType == SpecialType.System_String
            || symbols.IsQueryableType(collection.Type))
        {
            return;
        }

        var count = GetElementCount(collection);
        if (count is null || count.Value < ReportThreshold)
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
            EfLintDiagnostics.LargeContains,
            location,
            count.Value.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>True when the invocation sits in a lambda passed to an EF query operator.</summary>
    private static bool IsInsideEfQueryLambda(IOperation operation, EfSymbols symbols)
    {
        IOperation current = operation;
        while (current.Parent is not null)
        {
            current = current.Parent;
            if (current is not IAnonymousFunctionOperation)
            {
                continue;
            }

            IOperation? parent = current.Parent;
            while (parent is IConversionOperation or IParenthesizedOperation or IDelegateCreationOperation)
            {
                parent = parent.Parent;
            }

            if (parent is not IArgumentOperation argument
                || argument.Parent is not IInvocationOperation queryCall)
            {
                continue;
            }

            var queryMethod = queryCall.TargetMethod;
            if (!QueryChain.IsQueryOperatorNamed(queryMethod, symbols, queryMethod.Name)
                && !QueryChain.IsMaterializer(queryMethod, symbols))
            {
                continue;
            }

            var source = queryCall.Instance
                ?? queryCall.Arguments.FirstOrDefault(a => symbols.IsQueryableType(a.Parameter?.Type))?.Value;
            if (source is not null && QueryChain.IsEfQuery(source, symbols))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The in-memory collection a Contains call reads its candidate values from.</summary>
    private static IOperation? GetCollection(IInvocationOperation invocation)
    {
        if (invocation.Instance is not null)
        {
            return RawSqlHelpers.Unwrap(invocation.Instance);
        }

        // Static Enumerable.Contains(source, value): the source is the collection.
        var containing = invocation.TargetMethod.ReducedFrom?.ContainingType
            ?? invocation.TargetMethod.ContainingType;
        if (containing is not null
            && containing.Name == "Enumerable"
            && containing.ContainingNamespace?.Name == "Linq"
            && containing.ContainingNamespace.ContainingNamespace?.Name == "System"
            && invocation.Arguments.Length > 0)
        {
            return RawSqlHelpers.Unwrap(invocation.Arguments[0].Value);
        }

        return null;
    }

    /// <summary>Number of literal values in an in-memory collection, or null when unknown.</summary>
    private static int? GetElementCount(IOperation collection)
    {
        switch (RawSqlHelpers.Unwrap(collection))
        {
            case IArrayCreationOperation array:
                return array.Initializer?.ElementValues.Length;

            case IArrayInitializerOperation initializer:
                return initializer.ElementValues.Length;

            case IObjectCreationOperation creation
                when creation.Initializer is IObjectOrCollectionInitializerOperation objectInitializer:
                return objectInitializer.Initializers.Length;

            case ILocalReferenceOperation local:
                return GetLocalInitializerCount(local);

            default:
                return null;
        }
    }

    private static int? GetLocalInitializerCount(ILocalReferenceOperation reference)
    {
        IOperation? current = reference;
        while (current.Parent is not null)
        {
            current = current.Parent;
            if (current is not (IAnonymousFunctionOperation
                or ILocalFunctionOperation
                or IMethodBodyOperation
                or IConstructorBodyOperation))
            {
                continue;
            }

            foreach (var node in current.DescendantsAndSelf())
            {
                if (node is IVariableDeclaratorOperation declarator
                    && SymbolEqualityComparer.Default.Equals(declarator.Symbol, reference.Local)
                    && declarator.Initializer is not null
                    && declarator.Syntax.SpanStart <= reference.Syntax.SpanStart)
                {
                    return GetElementCount(declarator.Initializer.Value);
                }
            }
        }

        return null;
    }
}
