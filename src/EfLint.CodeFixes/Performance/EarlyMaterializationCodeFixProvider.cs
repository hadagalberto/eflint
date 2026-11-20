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

/// <summary>Moves an early materializer to the end of its query chain.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EarlyMaterializationCodeFixProvider)), Shared]
public sealed class EarlyMaterializationCodeFixProvider : CodeFixProvider
{
    private const string EquivalenceKey = "EFL1001.MoveMaterialization";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(EfLintDiagnostics.EarlyMaterializationId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            var invocation = FindInvocation(root, diagnostic.Location.SourceSpan);
            if (invocation is null || !CanFix(invocation))
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
        if (root is null)
        {
            return document;
        }

        var invocation = FindInvocation(root, diagnostic.Location.SourceSpan);
        if (invocation is null || !CanFix(invocation))
        {
            return document;
        }

        var member = (MemberAccessExpressionSyntax)invocation.Expression;
        var source = member.Expression;
        var name = GetMaterializerName(invocation);

        var destination = FindOutermostInvocation(invocation);
        if (destination is null)
        {
            return document;
        }

        var moved = destination.ReplaceNode(invocation, source.WithTriviaFrom(invocation));
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

    private static bool CanFix(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax
        && invocation.ArgumentList.Arguments.Count == 0;

    private static string GetMaterializerName(InvocationExpressionSyntax invocation) =>
        ((MemberAccessExpressionSyntax)invocation.Expression).Name.Identifier.ValueText;

    private static InvocationExpressionSyntax? FindOutermostInvocation(InvocationExpressionSyntax materializer)
    {
        InvocationExpressionSyntax? destination = null;
        ExpressionSyntax current = materializer;

        while (current.Parent is MemberAccessExpressionSyntax member
            && member.Expression == current
            && member.Parent is InvocationExpressionSyntax invocation
            && invocation.Expression == member)
        {
            destination = invocation;
            current = invocation;
        }

        return destination;
    }

    private static InvocationExpressionSyntax? FindInvocation(SyntaxNode root, TextSpan span) =>
        root.FindNode(span, getInnermostNodeForTie: true)
            .FirstAncestorOrSelf<InvocationExpressionSyntax>();
}
