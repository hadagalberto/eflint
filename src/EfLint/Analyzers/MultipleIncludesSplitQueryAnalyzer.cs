using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using EfLint.Infrastructure;

namespace EfLint.Analyzers;

/// <summary>
/// EFL1010: several collection Includes in one query produce a cartesian
/// explosion unless the query is executed as split queries.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MultipleIncludesSplitQueryAnalyzer : DiagnosticAnalyzer
{
    private const int MaxChainDepth = 64;

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(EfLintDiagnostics.MultipleIncludesSplitQuery);

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

        var receiver = GetQuerySource(invocation, symbols);
        if (receiver is null || !QueryChain.IsEfQuery(receiver, symbols))
        {
            return;
        }

        // Inspect the whole query chain this Include belongs to. Walking up only
        // through invocation parents keeps nested queries in lambdas out of scope.
        var outermost = FindOutermostInvocation(invocation);

        var collectionKeys = new HashSet<string>(StringComparer.Ordinal);
        IInvocationOperation? lastCollectionInclude = null;
        var hasSplit = false;
        var hasSingle = false;

        // Walk from the outermost operator (materializer or last query operator)
        // back towards the DbSet. The first collection Include seen this way is
        // the last one in source order.
        var current = RawSqlHelpers.Unwrap(outermost);
        for (var depth = 0; depth < MaxChainDepth && current is not null; depth++)
        {
            if (current is IInvocationOperation chainInvocation)
            {
                if (QueryChain.IsQueryOperatorNamed(chainInvocation.TargetMethod, symbols, "AsSplitQuery"))
                {
                    hasSplit = true;
                }
                else if (QueryChain.IsQueryOperatorNamed(chainInvocation.TargetMethod, symbols, "AsSingleQuery"))
                {
                    hasSingle = true;
                }
                else if (QueryChain.IsQueryOperatorNamed(chainInvocation.TargetMethod, symbols, "Include"))
                {
                    var key = GetCollectionKey(chainInvocation, GetQuerySource(chainInvocation, symbols), symbols);
                    if (key is not null && collectionKeys.Add(key))
                    {
                        lastCollectionInclude ??= chainInvocation;
                    }
                }

                var next = GetQuerySource(chainInvocation, symbols);
                if (next is null)
                {
                    break;
                }

                current = RawSqlHelpers.Unwrap(next);
                continue;
            }

            if (current is IMemberReferenceOperation member && member.Instance is not null)
            {
                current = RawSqlHelpers.Unwrap(member.Instance);
                continue;
            }

            break;
        }

        if (hasSplit || hasSingle || collectionKeys.Count < 2 || lastCollectionInclude is null
            || !IsSameInvocation(lastCollectionInclude, invocation))
        {
            return;
        }

        var options = EfLintOptions.Create(context.Options, invocation.Syntax.SyntaxTree);
        if (!options.IsRuleEnabled(EfLintDiagnostics.MultipleIncludesSplitQueryId))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            EfLintDiagnostics.MultipleIncludesSplitQuery,
            GetNameLocation(invocation)));
    }

    /// <summary>
    /// Returns a stable key for a collection Include, or null for a reference
    /// navigation / an Include whose path cannot be resolved.
    /// </summary>
    private static string? GetCollectionKey(
        IInvocationOperation include,
        IOperation? receiver,
        EfSymbols symbols)
    {
        foreach (var argument in include.Arguments)
        {
            var value = RawSqlHelpers.Unwrap(argument.Value);

            // Include("Posts") — resolve the string against the query's entity type.
            if (value.ConstantValue.HasValue && value.ConstantValue.Value is string path)
            {
                var entityType = GetEntityType(receiver?.Type, symbols);
                if (entityType is null)
                {
                    return null;
                }

                foreach (var member in entityType.GetMembers(path))
                {
                    var memberType = member switch
                    {
                        IPropertySymbol property => property.Type,
                        IFieldSymbol field => field.Type,
                        _ => null,
                    };

                    if (memberType is not null && IsCollectionType(memberType, symbols))
                    {
                        return path;
                    }
                }

                return null;
            }

            // Include(b => b.Posts) — inspect the navigation returned by the lambda.
            if (value is IAnonymousFunctionOperation lambda
                && lambda.Symbol.Parameters.Length > 0)
            {
                var parameter = lambda.Symbol.Parameters[0];
                foreach (var node in lambda.Body.DescendantsAndSelf())
                {
                    if (RawSqlHelpers.Unwrap(node) is IMemberReferenceOperation member
                        && IsParameterReference(member.Instance, parameter))
                    {
                        return member.Type is not null && IsCollectionType(member.Type, symbols)
                            ? member.Member.Name
                            : null;
                    }
                }
            }
        }

        return null;
    }

    private static bool IsParameterReference(IOperation? operation, IParameterSymbol parameter)
    {
        if (operation is null)
        {
            return false;
        }

        return RawSqlHelpers.Unwrap(operation) is IParameterReferenceOperation reference
            && SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter);
    }

    private static INamedTypeSymbol? GetEntityType(ITypeSymbol? queryType, EfSymbols symbols)
    {
        if (queryType is null)
        {
            return null;
        }

        if (queryType is INamedTypeSymbol named
            && named.TypeArguments.Length == 1
            && (symbols.IsQueryableType(queryType) || symbols.IsDbSetType(queryType)))
        {
            return named.TypeArguments[0] as INamedTypeSymbol;
        }

        foreach (var iface in queryType.AllInterfaces)
        {
            if (symbols.IsQueryableType(iface)
                && iface is INamedTypeSymbol namedInterface
                && namedInterface.TypeArguments.Length == 1)
            {
                return namedInterface.TypeArguments[0] as INamedTypeSymbol;
            }
        }

        return null;
    }

    private static bool IsCollectionType(ITypeSymbol type, EfSymbols symbols)
    {
        // string implements IEnumerable<char> but is a scalar, not a navigation.
        if (type.SpecialType == SpecialType.System_String)
        {
            return false;
        }

        if (type is IArrayTypeSymbol || symbols.IEnumerable is null)
        {
            return type is IArrayTypeSymbol;
        }

        if (SymbolEqualityComparer.Default.Equals(type, symbols.IEnumerable))
        {
            return true;
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(iface, symbols.IEnumerable))
            {
                return true;
            }
        }

        return false;
    }

    private static IInvocationOperation FindOutermostInvocation(IOperation operation)
    {
        IOperation current = operation;
        while (true)
        {
            var parent = current.Parent;

            // Receiver conversions (common with generic extension methods) must be
            // crossed before the enclosing invocation becomes visible.
            if (parent is IConversionOperation or IParenthesizedOperation)
            {
                current = parent;
                continue;
            }

            // Reduced extension methods may expose the receiver as the first
            // argument instead of Instance; only climb through it when this
            // operation really is the query source of the outer invocation.
            if (parent is IArgumentOperation argument
                && argument.Parent is IInvocationOperation argumentOwner
                && argumentOwner.Arguments.Length > 0
                && ReferenceEquals(argumentOwner.Arguments[0], argument))
            {
                current = argumentOwner;
                continue;
            }

            if (parent is IInvocationOperation invocation)
            {
                current = invocation;
                continue;
            }

            break;
        }

        return (IInvocationOperation)RawSqlHelpers.Unwrap(current);
    }

    private static IOperation? GetQuerySource(IInvocationOperation invocation, EfSymbols symbols)
    {
        if (invocation.Instance is not null)
        {
            return invocation.Instance;
        }

        foreach (var argument in invocation.Arguments)
        {
            var value = RawSqlHelpers.Unwrap(argument.Value);
            if (symbols.IsQueryableType(value.Type) || symbols.IsDbSetType(value.Type))
            {
                return value;
            }
        }

        return null;
    }

    private static bool IsSameInvocation(IInvocationOperation left, IInvocationOperation right)
    {
        return ReferenceEquals(left, right)
            || (left.Syntax.SyntaxTree == right.Syntax.SyntaxTree
                && left.Syntax.Span == right.Syntax.Span);
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
