using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using EfLint.Analyzers;

namespace EfLint.CodeFixes.Performance;

/// <summary>Moves an early materializer past the query operators that can run on the server.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EarlyMaterializationCodeFixProvider)), Shared]
public sealed class EarlyMaterializationCodeFixProvider : CodeFixProvider
{
    private const string EquivalenceKey = "EFL1001.MoveMaterialization";

    /// <summary>Operators that return a sequence, so the materializer can be appended after them.</summary>
    private static readonly HashSet<string> SequenceOperators = new()
    {
        "Where", "Select", "SelectMany", "OrderBy", "OrderByDescending",
        "ThenBy", "ThenByDescending", "Skip", "Take",
    };

    /// <summary>Materializers already at the end of a chain; appending another would duplicate it.</summary>
    private static readonly HashSet<string> MaterializerNames = new()
    {
        "ToList", "ToArray", "ToHashSet", "ToDictionary", "ToLookup",
        "First", "FirstOrDefault", "Single", "SingleOrDefault",
        "Last", "LastOrDefault", "ElementAt", "ElementAtOrDefault",
        "Count", "Any", "LongCount", "All",
    };

    /// <summary>Operators safe to move the materializer past.</summary>
    private static readonly HashSet<string> SafeOperators = new()
    {
        "Where", "Select", "SelectMany", "OrderBy", "OrderByDescending",
        "ThenBy", "ThenByDescending", "Skip", "Take",
        "Count", "Any", "LongCount", "All",
    };

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(EfLintDiagnostics.EarlyMaterializationId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            var invocation = FindInvocation(root, diagnostic.Location.SourceSpan);
            if (invocation is null || !CanFix(invocation, model))
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Move '{GetMaterializerName(invocation)}' to the end of the query",
                    cancellationToken => FixAsync(context.Document, diagnostic, cancellationToken),
                    equivalenceKey: EquivalenceKey),
                diagnostic);
        }
    }

    private static async Task<Document> FixAsync(
        Document document,
        Diagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return document;
        }

        var invocation = FindInvocation(root, diagnostic.Location.SourceSpan);
        if (invocation is null || !CanFix(invocation, model))
        {
            return document;
        }

        var member = (MemberAccessExpressionSyntax)invocation.Expression;
        var source = member.Expression;
        var name = GetMaterializerName(invocation);

        var destination = FindDestination(invocation, model);
        if (destination is null)
        {
            return document;
        }

        // Remove the early materializer. If the destination is itself a terminal operator
        // (Count/Any or an existing ToList/ToArray), appending again would produce invalid
        // code such as db.Blogs.Count().ToList() or .Where(...).ToList().ToList().
        var moved = destination.ReplaceNode(invocation, source.WithTriviaFrom(invocation));
        if (!NeedsMaterializer(destination, model) || HasOuterMaterializer(destination, model))
        {
            return document.WithSyntaxRoot(root.ReplaceNode(destination, moved));
        }

        var leading = moved.GetLeadingTrivia();
        var trailing = moved.GetTrailingTrivia();

        var appended = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                moved.WithoutTrivia(),
                SyntaxFactory.IdentifierName(name)),
            SyntaxFactory.ArgumentList())
            .WithLeadingTrivia(leading)
            .WithTrailingTrivia(trailing);

        return document.WithSyntaxRoot(root.ReplaceNode(destination, appended));
    }

    private static bool CanFix(InvocationExpressionSyntax invocation, SemanticModel model) =>
        invocation.Expression is MemberAccessExpressionSyntax
        && invocation.ArgumentList.Arguments.Count == 0
        && FindDestination(invocation, model) is not null;

    private static string GetMaterializerName(InvocationExpressionSyntax invocation) =>
        ((MemberAccessExpressionSyntax)invocation.Expression).Name.Identifier.ValueText;

    /// <summary>
    /// Finds the innermost chain of operators the materializer can safely pass. Stops before
    /// the first operator whose lambda calls a source-declared (non-translatable) method.
    /// </summary>
    private static InvocationExpressionSyntax? FindDestination(
        InvocationExpressionSyntax materializer,
        SemanticModel model)
    {
        InvocationExpressionSyntax? destination = null;
        ExpressionSyntax current = materializer;

        while (current.Parent is MemberAccessExpressionSyntax member
            && member.Expression == current
            && member.Parent is InvocationExpressionSyntax invocation
            && invocation.Expression == member)
        {
            if (!IsSafeOperator(invocation, model))
            {
                break;
            }

            destination = invocation;
            current = invocation;
        }

        return destination;
    }

    private static bool IsSafeOperator(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
            || !SafeOperators.Contains(method.Name))
        {
            return false;
        }

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.Expression is not LambdaExpressionSyntax lambda)
            {
                continue;
            }

            foreach (var node in lambda.DescendantNodesAndSelf())
            {
                if (node is not InvocationExpressionSyntax call
                    || model.GetSymbolInfo(call).Symbol is not IMethodSymbol callee
                    || callee.DeclaringSyntaxReferences.Length == 0
                    || HasDbFunctionAttribute(callee))
                {
                    continue;
                }

                return false;
            }
        }

        return true;
    }

    private static bool HasDbFunctionAttribute(IMethodSymbol method)
    {
        foreach (var attribute in method.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "DbFunctionAttribute")
            {
                return true;
            }
        }

        return false;
    }

    private static bool NeedsMaterializer(InvocationExpressionSyntax destination, SemanticModel model)
    {
        return model.GetSymbolInfo(destination).Symbol is IMethodSymbol method
            && SequenceOperators.Contains(method.Name);
    }

    /// <summary>
    /// True when a later invocation in the same chain already materializes the query. Removing
    /// the early one is then enough; appending another would produce .ToList().ToList().
    /// </summary>
    private static bool HasOuterMaterializer(InvocationExpressionSyntax destination, SemanticModel model)
    {
        ExpressionSyntax current = destination;
        while (current.Parent is MemberAccessExpressionSyntax member
            && member.Expression == current
            && member.Parent is InvocationExpressionSyntax invocation
            && invocation.Expression == member)
        {
            if (model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
                && MaterializerNames.Contains(method.Name))
            {
                return true;
            }

            current = invocation;
        }

        return false;
    }

    private static InvocationExpressionSyntax? FindInvocation(SyntaxNode root, TextSpan span) =>
        root.FindNode(span, getInnermostNodeForTie: true)
            .FirstAncestorOrSelf<InvocationExpressionSyntax>();
}
