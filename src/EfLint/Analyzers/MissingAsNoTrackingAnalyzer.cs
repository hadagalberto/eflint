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
/// EFL1003: an EF Core query is materialized without AsNoTracking even though
/// the enclosing method never modifies the results or saves changes.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingAsNoTrackingAnalyzer : DiagnosticAnalyzer
{
    private const int MaxChainDepth = 64;

    private static readonly HashSet<string> MutationNames = new()
    {
        "SaveChanges", "SaveChangesAsync",
        "Update", "UpdateRange",
        "Add", "AddRange",
        "Remove", "RemoveRange",
        "Attach",
    };

    /// <summary>
    /// Materializers whose result is a scalar or an aggregate. No entity is handed to the
    /// caller, so AsNoTracking() would either be inapplicable (CS0452) or pointless.
    /// </summary>
    private static readonly HashSet<string> ScalarMaterializers = new()
    {
        "Count", "CountAsync", "LongCount", "LongCountAsync",
        "Any", "AnyAsync", "All", "AllAsync",
        "Sum", "SumAsync", "Min", "MinAsync", "Max", "MaxAsync",
        "Average", "AverageAsync", "Aggregate", "Contains",
        "ExecuteDelete", "ExecuteDeleteAsync", "ExecuteUpdate", "ExecuteUpdateAsync",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(EfLintDiagnostics.MissingAsNoTracking);

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
        if (!QueryChain.IsMaterializer(invocation.TargetMethod, symbols)
            || IsScalarMaterializer(invocation.TargetMethod))
        {
            return;
        }

        // A materializer inside a lambda is a subquery, not a standalone read-only query.
        if (IsInsideLambda(invocation))
        {
            return;
        }

        var source = invocation.Instance ?? invocation.Arguments.FirstOrDefault()?.Value;
        if (source is null || !QueryChain.IsEfQuery(source, symbols))
        {
            return;
        }

        // The whole point of AsNoTracking is to avoid tracking materialized entities. If the
        // query element is a scalar, an anonymous type or any type we cannot prove is a mapped
        // entity, adding AsNoTracking() changes nothing or does not compile. Stay silent.
        if (!MaterializesEntity(source, invocation.Type, symbols))
        {
            return;
        }

        if (HasTrackingConfiguration(source, symbols))
        {
            return;
        }

        // Deliberately conservative: when the method cannot be inspected or any
        // write-like operation is present, stay silent rather than emit a false positive.
        if (HasMutatingOperations(invocation, symbols))
        {
            return;
        }

        var options = EfLintOptions.Create(context.Options, invocation.Syntax.SyntaxTree);
        if (!options.IsRuleEnabled(EfLintDiagnostics.MissingAsNoTrackingId))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            EfLintDiagnostics.MissingAsNoTracking,
            GetNameLocation(invocation)));
    }

    private static bool IsScalarMaterializer(IMethodSymbol method) =>
        ScalarMaterializers.Contains(method.Name);

    private static bool IsInsideLambda(IOperation operation)
    {
        for (var parent = operation.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True only when the materialized element is a class we can prove is a mapped entity:
    /// either the type of a DbSet/Set&lt;T&gt; root or a navigation of that root entity.
    /// </summary>
    private static bool MaterializesEntity(IOperation source, ITypeSymbol? resultType, EfSymbols symbols)
    {
        if (resultType is null || resultType.IsValueType || resultType.SpecialType == SpecialType.System_String)
        {
            return false;
        }

        var elementType = GetQueryElementType(source);
        if (elementType is null || !IsMappedEntityCandidate(elementType))
        {
            return false;
        }

        var rootEntityType = FindRootEntityType(source, symbols);
        if (rootEntityType is null)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(elementType, rootEntityType)
            || IsNavigationOf(rootEntityType, elementType);
    }

    private static ITypeSymbol? GetQueryElementType(IOperation source)
    {
        var type = RawSqlHelpers.Unwrap(source).Type;
        if (type is null)
        {
            return null;
        }

        if (TryGetQueryElement(type, out var element))
        {
            return element;
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (TryGetQueryElement(iface, out element))
            {
                return element;
            }
        }

        return null;
    }

    private static bool TryGetQueryElement(ITypeSymbol type, out ITypeSymbol? element)
    {
        element = null;
        if (type is not INamedTypeSymbol named || !named.IsGenericType || named.TypeArguments.Length != 1)
        {
            return false;
        }

        var ns = named.ContainingNamespace?.ToDisplayString();
        var definition = named.OriginalDefinition.Name;
        var isQueryElement = definition switch
        {
            "IQueryable" or "IOrderedQueryable" => ns == "System.Linq",
            "IEnumerable" or "ICollection" or "IList" or "IReadOnlyCollection" or "IReadOnlyList"
                => ns == "System.Collections.Generic",
            "DbSet" => ns == "Microsoft.EntityFrameworkCore",
            _ => false,
        };

        if (!isQueryElement)
        {
            return false;
        }

        element = named.TypeArguments[0];
        return true;
    }

    private static bool IsMappedEntityCandidate(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol named
            && named.TypeKind == TypeKind.Class
            && !named.IsAnonymousType
            && named.SpecialType == SpecialType.None;
    }

    /// <summary>Walks a query chain down to the DbSet/Set&lt;T&gt; that roots it.</summary>
    private static INamedTypeSymbol? FindRootEntityType(IOperation operation, EfSymbols symbols)
    {
        IOperation? current = RawSqlHelpers.Unwrap(operation);
        for (var depth = 0; depth < MaxChainDepth && current is not null; depth++)
        {
            if (symbols.IsDbSetType(current.Type)
                && current.Type is INamedTypeSymbol setType
                && setType.TypeArguments.Length == 1)
            {
                return setType.TypeArguments[0] as INamedTypeSymbol;
            }

            if (current is IInvocationOperation setCall
                && setCall.TargetMethod.Name == "Set"
                && (symbols.IsDbContextType(setCall.Instance?.Type)
                    || symbols.IsDbContextType(setCall.TargetMethod.ContainingType?.OriginalDefinition))
                && setCall.TargetMethod.TypeArguments.Length == 1)
            {
                return setCall.TargetMethod.TypeArguments[0] as INamedTypeSymbol;
            }

            if (current is ILocalReferenceOperation local)
            {
                var function = EfOperationHelpers.FindEnclosingFunction(local);
                var value = function is null
                    ? null
                    : EfOperationHelpers.FindLastValueForSymbol(function, local.Local, local.Syntax.SpanStart);
                if (value is null)
                {
                    return null;
                }

                current = RawSqlHelpers.Unwrap(value);
                continue;
            }

            // A source-declared factory returning IQueryable<T> is an EF query only when its
            // own return expression roots in a DbSet/Set<T>; inspect it the same way.
            if (current is IInvocationOperation factoryCall
                && factoryCall.TargetMethod.DeclaringSyntaxReferences.Length > 0
                && (symbols.IsQueryableType(factoryCall.TargetMethod.ReturnType)
                    || symbols.IsDbSetType(factoryCall.TargetMethod.ReturnType)))
            {
                var syntaxReference = factoryCall.TargetMethod.DeclaringSyntaxReferences[0];
                var declarationModel = factoryCall.SemanticModel;
                if (declarationModel is null || declarationModel.SyntaxTree != syntaxReference.SyntaxTree)
                {
                    return null;
                }

                var declaration = syntaxReference.GetSyntax();
                foreach (var syntax in declaration.DescendantNodes())
                {
                    SyntaxNode? expression = syntax switch
                    {
                        ReturnStatementSyntax { Expression: not null } statement => statement.Expression,
                        ArrowExpressionClauseSyntax arrow => arrow.Expression,
                        _ => null,
                    };

                    if (expression is null)
                    {
                        continue;
                    }

                    var returned = declarationModel.GetOperation(expression);
                    if (returned is null)
                    {
                        continue;
                    }

                    var returnedRoot = FindRootEntityType(returned, symbols);
                    if (returnedRoot is not null)
                    {
                        return returnedRoot;
                    }
                }

                return null;
            }

            switch (current)
            {
                case IInvocationOperation invocation when invocation.Instance is not null:
                    current = RawSqlHelpers.Unwrap(invocation.Instance);
                    continue;

                case IInvocationOperation invocation:
                    var queryableArgument = invocation.Arguments.FirstOrDefault(argument =>
                        symbols.IsQueryableType(argument.Parameter?.Type)
                        || symbols.IsQueryableType(argument.Value.Type)
                        || symbols.IsDbSetType(argument.Value.Type));
                    if (queryableArgument is null)
                    {
                        return null;
                    }

                    current = RawSqlHelpers.Unwrap(queryableArgument.Value);
                    continue;

                case IMemberReferenceOperation member when member.Instance is not null:
                    current = RawSqlHelpers.Unwrap(member.Instance);
                    continue;

                default:
                    return null;
            }
        }

        return null;
    }

    private static bool IsNavigationOf(INamedTypeSymbol entityType, ITypeSymbol candidate)
    {
        for (var type = entityType; type is not null; type = type.BaseType)
        {
            foreach (var member in type.GetMembers())
            {
                ITypeSymbol? memberType = member switch
                {
                    IPropertySymbol property when !property.IsStatic => property.Type,
                    IFieldSymbol field when !field.IsStatic => field.Type,
                    _ => null,
                };

                if (memberType is null)
                {
                    continue;
                }

                if (SymbolEqualityComparer.Default.Equals(memberType, candidate))
                {
                    return true;
                }

                if (memberType is INamedTypeSymbol named)
                {
                    foreach (var argument in named.TypeArguments)
                    {
                        if (SymbolEqualityComparer.Default.Equals(argument, candidate))
                        {
                            return true;
                        }
                    }
                }

                foreach (var iface in memberType.AllInterfaces)
                {
                    if (iface is not INamedTypeSymbol interfaceType)
                    {
                        continue;
                    }

                    foreach (var argument in interfaceType.TypeArguments)
                    {
                        if (SymbolEqualityComparer.Default.Equals(argument, candidate))
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Walks the query source chain and detects an explicit tracking choice.
    /// AsTracking/AsTrackingWithIdentityResolution are treated as opt-outs too:
    /// suggesting AsNoTracking against an explicit AsTracking would be wrong.
    /// </summary>
    private static bool HasTrackingConfiguration(IOperation source, EfSymbols symbols)
    {
        IOperation? current = source;
        for (var depth = 0; depth < MaxChainDepth && current is not null; depth++)
        {
            // Extension calls such as AsNoTracking are often wrapped in an
            // IConversionOperation when used as a query source.
            current = RawSqlHelpers.Unwrap(current);
            switch (current)
            {
                case IInvocationOperation invocation:
                    if (IsTrackingConfiguration(invocation.TargetMethod, symbols))
                    {
                        return true;
                    }

                    if (invocation.Instance is not null)
                    {
                        current = invocation.Instance;
                        continue;
                    }

                    var queryableArgument = invocation.Arguments.FirstOrDefault(
                        argument => symbols.IsQueryableType(argument.Parameter?.Type));
                    if (queryableArgument is null)
                    {
                        return false;
                    }

                    current = queryableArgument.Value;
                    continue;

                case IMemberReferenceOperation member when member.Instance is not null:
                    current = member.Instance;
                    continue;

                case IConditionalOperation conditional:
                    return (conditional.WhenTrue is not null && HasTrackingConfiguration(conditional.WhenTrue, symbols))
                        || (conditional.WhenFalse is not null && HasTrackingConfiguration(conditional.WhenFalse, symbols));

                default:
                    return false;
            }
        }

        return false;
    }

    private static bool IsTrackingConfiguration(IMethodSymbol method, EfSymbols symbols)
    {
        return QueryChain.IsQueryOperatorNamed(method, symbols, "AsNoTracking")
            || QueryChain.IsQueryOperatorNamed(method, symbols, "AsNoTrackingWithIdentityResolution")
            || QueryChain.IsQueryOperatorNamed(method, symbols, "AsTracking")
            || QueryChain.IsQueryOperatorNamed(method, symbols, "AsTrackingWithIdentityResolution");
    }

    /// <summary>
    /// Scans the enclosing function for writes that make no-tracking unsafe:
    /// context/DbSet mutations and assignments to a member of a local.
    /// </summary>
    private static bool HasMutatingOperations(IOperation invocation, EfSymbols symbols)
    {
        var function = EfOperationHelpers.FindEnclosingFunction(invocation);
        if (function is null)
        {
            return true;
        }

        foreach (var node in function.DescendantsAndSelf())
        {
            if (node is IInvocationOperation candidate
                && MutationNames.Contains(candidate.TargetMethod.Name)
                && IsContextOrSetMethod(candidate, symbols))
            {
                return true;
            }

            if (node is ISimpleAssignmentOperation assignment
                && assignment.Target is IMemberReferenceOperation member
                && member.Instance is ILocalReferenceOperation)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsContextOrSetMethod(IInvocationOperation invocation, EfSymbols symbols)
    {
        var method = invocation.TargetMethod;
        return symbols.IsDbContextType(method.ContainingType)
            || symbols.IsDbSetType(method.ContainingType)
            || symbols.IsDbContextType(invocation.Instance?.Type)
            || symbols.IsDbSetType(invocation.Instance?.Type);
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
