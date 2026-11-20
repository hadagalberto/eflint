using System;
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
/// EFL1004: accessing an unincluded navigation, or issuing a query that depends on
/// the current item, inside a foreach over EF query results produces one database
/// round trip per iteration — the N+1 pattern.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NPlusOneAnalyzer : DiagnosticAnalyzer
{
    private const int MaxChainDepth = 64;

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(EfLintDiagnostics.NPlusOne);

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

            // IForEachLoopOperation reports OperationKind.Loop.
            startContext.RegisterOperationAction(
                operationContext => AnalyzeLoop(operationContext, symbols),
                OperationKind.Loop);
        });
    }

    private static void AnalyzeLoop(OperationAnalysisContext context, EfSymbols symbols)
    {
        if (context.Operation is not IForEachLoopOperation loop
            || loop.Collection is null
            || loop.Body is null)
        {
            return;
        }

        var loopVariable = GetLoopVariable(loop);
        if (loopVariable is null)
        {
            return;
        }

        if (!TryAnalyzeCollection(loop.Collection, symbols, out var included))
        {
            return;
        }

        var options = EfLintOptions.Create(context.Options, loop.Syntax.SyntaxTree);
        if (!options.IsRuleEnabled(EfLintDiagnostics.NPlusOneId))
        {
            return;
        }

        // First navigation finding wins. For per-item queries keep the innermost
        // matching invocation (DescendantsAndSelf is pre-order), so a query such as
        // `db.Posts.Where(p => p.BlogId == b.Id).Count()` is reported on `Where`,
        // the operator that actually depends on the loop item.
        IInvocationOperation? perItemQuery = null;
        foreach (var node in loop.Body.DescendantsAndSelf())
        {
            switch (node)
            {
                case IMemberReferenceOperation member
                    when IsLoopVariableReference(member.Instance, loopVariable)
                         && IsNavigation(member)
                         && !included.Contains(member.Member.Name):
                    context.ReportDiagnostic(Diagnostic.Create(
                        EfLintDiagnostics.NPlusOne,
                        GetMemberNameLocation(member),
                        $"Navigation '{member.Member.Name}' is accessed per item"));
                    return;

                case IInvocationOperation invocation
                    when IsEfRootedInvocation(invocation, symbols)
                         && ReferencesLoopVariable(invocation, loopVariable):
                    perItemQuery = invocation;
                    break;
            }
        }

        if (perItemQuery is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                EfLintDiagnostics.NPlusOne,
                GetInvocationNameLocation(perItemQuery),
                "A query depends on the loop item"));
        }
    }

    private static ISymbol? GetLoopVariable(IForEachLoopOperation loop)
    {
        var variable = loop.LoopControlVariable;
        if (variable is null)
        {
            return null;
        }

        var value = RawSqlHelpers.Unwrap(variable);
        if (value is IVariableDeclaratorOperation declarator)
        {
            return declarator.Symbol;
        }

        if (value is ILocalReferenceOperation local)
        {
            return local.Local;
        }

        return value is IParameterReferenceOperation parameter
            ? parameter.Parameter
            : null;
    }

    /// <summary>
    /// True when the foreach collection is rooted in an EF query (either the
    /// IQueryable itself or a materializer such as ToList over one) and collects
    /// the navigation names eagerly loaded by Include calls in the chain.
    /// </summary>
    private static bool TryAnalyzeCollection(
        IOperation collection,
        EfSymbols symbols,
        out HashSet<string> included)
    {
        included = new HashSet<string>(StringComparer.Ordinal);
        var current = RawSqlHelpers.Unwrap(collection);

        if (QueryChain.IsEfQuery(current, symbols))
        {
            CollectIncludes(current, symbols, included);
            return true;
        }

        if (current is IInvocationOperation invocation
            && QueryChain.IsMaterializer(invocation.TargetMethod, symbols))
        {
            var receiver = GetReceiver(invocation, symbols);
            if (receiver is not null)
            {
                var source = RawSqlHelpers.Unwrap(receiver);
                if (QueryChain.IsEfQuery(source, symbols))
                {
                    CollectIncludes(source, symbols, included);
                    return true;
                }
            }
        }

        return false;
    }

    private static void CollectIncludes(
        IOperation operation,
        EfSymbols symbols,
        HashSet<string> included)
    {
        var current = RawSqlHelpers.Unwrap(operation);
        for (var depth = 0; depth < MaxChainDepth && current is not null; depth++)
        {
            if (current is IInvocationOperation invocation)
            {
                if (QueryChain.IsQueryOperatorNamed(invocation.TargetMethod, symbols, "Include"))
                {
                    AddIncludeNames(invocation, included);
                }

                var receiver = GetReceiver(invocation, symbols);
                if (receiver is null)
                {
                    return;
                }

                current = RawSqlHelpers.Unwrap(receiver);
                continue;
            }

            if (current is IMemberReferenceOperation member && member.Instance is not null)
            {
                current = RawSqlHelpers.Unwrap(member.Instance);
                continue;
            }

            return;
        }
    }

    private static void AddIncludeNames(IInvocationOperation invocation, HashSet<string> included)
    {
        foreach (var argument in invocation.Arguments)
        {
            var value = RawSqlHelpers.Unwrap(argument.Value);

            // Include("Posts") string path form.
            if (value.ConstantValue.HasValue && value.ConstantValue.Value is string path)
            {
                included.Add(path);
                continue;
            }

            // Include(b => b.Posts) lambda form.
            if (value is IAnonymousFunctionOperation lambda)
            {
                foreach (var node in lambda.Body.DescendantsAndSelf())
                {
                    if (RawSqlHelpers.Unwrap(node) is IMemberReferenceOperation member)
                    {
                        included.Add(member.Member.Name);
                        break;
                    }
                }
            }
        }
    }

    private static IOperation? GetReceiver(IInvocationOperation invocation, EfSymbols symbols)
    {
        if (invocation.Instance is not null)
        {
            return invocation.Instance;
        }

        foreach (var argument in invocation.Arguments)
        {
            if (symbols.IsQueryableType(argument.Parameter?.Type))
            {
                return argument.Value;
            }
        }

        // Reduced extension methods whose receiver parameter is only IEnumerable<T>
        // still place the receiver in the first argument.
        return invocation.Arguments.FirstOrDefault()?.Value;
    }

    private static bool IsLoopVariableReference(IOperation? instance, ISymbol loopVariable)
    {
        if (instance is null)
        {
            return false;
        }

        return RawSqlHelpers.Unwrap(instance) switch
        {
            ILocalReferenceOperation local =>
                SymbolEqualityComparer.Default.Equals(local.Local, loopVariable),
            IParameterReferenceOperation parameter =>
                SymbolEqualityComparer.Default.Equals(parameter.Parameter, loopVariable),
            _ => false,
        };
    }

    /// <summary>
    /// True when a member type is an entity declared in source, or a collection of
    /// such an entity — i.e. a navigation, not a scalar property.
    /// </summary>
    private static bool IsNavigation(IMemberReferenceOperation member)
    {
        var type = member.Type;
        if (type is null)
        {
            return false;
        }

        var candidate = GetEnumerableElementType(type) ?? type;
        return candidate is INamedTypeSymbol named
            && named.TypeKind == TypeKind.Class
            && named.DeclaringSyntaxReferences.Length > 0;
    }

    private static ITypeSymbol? GetEnumerableElementType(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String)
        {
            return null;
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (iface is INamedTypeSymbol named
                && named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
                && named.TypeArguments.Length == 1)
            {
                return named.TypeArguments[0];
            }
        }

        return null;
    }

    private static bool IsEfRootedInvocation(IInvocationOperation invocation, EfSymbols symbols)
    {
        var receiver = GetReceiver(invocation, symbols);
        if (receiver is null)
        {
            return false;
        }

        var source = RawSqlHelpers.Unwrap(receiver);
        return symbols.IsDbContextType(source.Type)
            || symbols.IsDbSetType(source.Type)
            || QueryChain.IsEfQuery(source, symbols);
    }

    private static bool ReferencesLoopVariable(IInvocationOperation invocation, ISymbol loopVariable)
    {
        foreach (var argument in invocation.Arguments)
        {
            foreach (var node in argument.Value.DescendantsAndSelf())
            {
                switch (node)
                {
                    case ILocalReferenceOperation local
                        when SymbolEqualityComparer.Default.Equals(local.Local, loopVariable):
                    case IParameterReferenceOperation parameter
                        when SymbolEqualityComparer.Default.Equals(parameter.Parameter, loopVariable):
                        return true;
                }
            }
        }

        return false;
    }

    private static Location GetMemberNameLocation(IMemberReferenceOperation member)
    {
        return member.Syntax switch
        {
            MemberAccessExpressionSyntax access => access.Name.GetLocation(),
            MemberBindingExpressionSyntax binding => binding.Name.GetLocation(),
            _ => member.Syntax.GetLocation(),
        };
    }

    private static Location GetInvocationNameLocation(IInvocationOperation invocation)
    {
        if (invocation.Syntax is InvocationExpressionSyntax syntax)
        {
            return syntax.Expression switch
            {
                MemberAccessExpressionSyntax access => access.Name.GetLocation(),
                MemberBindingExpressionSyntax binding => binding.Name.GetLocation(),
                SimpleNameSyntax simple => simple.GetLocation(),
                _ => syntax.GetLocation(),
            };
        }

        return invocation.Syntax.GetLocation();
    }
}
