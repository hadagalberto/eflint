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

namespace EfLint.CodeFixes.Correctness;

/// <summary>Passes an in-scope CancellationToken to an asynchronous EF operation.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingCancellationTokenCodeFixProvider)), Shared]
public sealed class MissingCancellationTokenCodeFixProvider : CodeFixProvider
{
    private const string EquivalenceKey = "EFL2002.PassCancellationToken";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(EfLintDiagnostics.MissingCancellationTokenId);

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
            if (invocation is null
                || !diagnostic.Properties.TryGetValue(MissingCancellationTokenAnalyzer.CancellationTokenProperty, out var token)
                || token is not { Length: > 0 })
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Pass '{token}' to the async operation",
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
        if (invocation is null
            || !diagnostic.Properties.TryGetValue(MissingCancellationTokenAnalyzer.CancellationTokenProperty, out var token)
            || token is not { Length: > 0 })
        {
            return document;
        }

        var arguments = new List<ArgumentSyntax>(invocation.ArgumentList.Arguments)
        {
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName(token)),
        };
        var updated = invocation.WithArgumentList(
            invocation.ArgumentList.WithArguments(SyntaxFactory.SeparatedList(arguments)));

        return document.WithSyntaxRoot(root.ReplaceNode(invocation, updated));
    }

    private static InvocationExpressionSyntax? FindInvocation(SyntaxNode root, TextSpan span)
    {
        return root.FindNode(span, getInnermostNodeForTie: true)
            .FirstAncestorOrSelf<InvocationExpressionSyntax>();
    }
}
