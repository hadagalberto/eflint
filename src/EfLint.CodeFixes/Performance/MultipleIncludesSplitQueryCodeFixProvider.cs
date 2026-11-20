using System.Collections.Immutable;
using System.Composition;
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

/// <summary>Adds AsSplitQuery() after the last Include of a query chain.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MultipleIncludesSplitQueryCodeFixProvider)), Shared]
public sealed class MultipleIncludesSplitQueryCodeFixProvider : CodeFixProvider
{
    private const string EquivalenceKey = "EFL1010.AsSplitQuery";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(EfLintDiagnostics.MultipleIncludesSplitQueryId);

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
            var reported = FindInvocation(root, diagnostic.Location.SourceSpan);
            if (reported is null || !IsInclude(model, reported, context.CancellationToken))
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Use AsSplitQuery() for multiple collection Includes",
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

        var reported = FindInvocation(root, diagnostic.Location.SourceSpan);
        if (reported is null)
        {
            return document;
        }

        var lastInclude = FindLastInclude(model, reported, cancellationToken);
        if (lastInclude is null)
        {
            return document;
        }

        var splitCall = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                lastInclude.WithoutTrivia(),
                SyntaxFactory.IdentifierName("AsSplitQuery")),
            SyntaxFactory.ArgumentList())
            .WithLeadingTrivia(lastInclude.GetLeadingTrivia())
            .WithTrailingTrivia(lastInclude.GetTrailingTrivia());

        return document.WithSyntaxRoot(root.ReplaceNode(lastInclude, splitCall));
    }

    /// <summary>
    /// Walks up the member-access/invocation pairs to the outermost Include in the
    /// chain, so the split operator is inserted after any trailing reference
    /// Includes and before later operators such as Where or ToList.
    /// </summary>
    private static InvocationExpressionSyntax? FindLastInclude(
        SemanticModel model,
        InvocationExpressionSyntax reported,
        CancellationToken cancellationToken)
    {
        var current = reported;
        while (current.Parent is MemberAccessExpressionSyntax member
            && member.Expression == current
            && member.Parent is InvocationExpressionSyntax outer
            && outer.Expression == member
            && IsIncludeLike(model, outer, cancellationToken))
        {
            current = outer;
        }

        return current;
    }

    private static bool IsInclude(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        return model.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol method
            && method.Name == "Include";
    }

    private static bool IsIncludeLike(
        SemanticModel model,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        return model.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol method
            && method.Name is "Include" or "ThenInclude";
    }

    private static InvocationExpressionSyntax? FindInvocation(SyntaxNode root, TextSpan span)
    {
        return root.FindNode(span, getInnermostNodeForTie: true)
            .FirstAncestorOrSelf<InvocationExpressionSyntax>();
    }
}
