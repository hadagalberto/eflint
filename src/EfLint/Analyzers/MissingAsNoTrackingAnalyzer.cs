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
        if (!QueryChain.IsMaterializer(invocation.TargetMethod, symbols))
        {
            return;
        }

        var source = invocation.Instance ?? invocation.Arguments.FirstOrDefault()?.Value;
        if (source is null || !QueryChain.IsEfQuery(source, symbols))
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
