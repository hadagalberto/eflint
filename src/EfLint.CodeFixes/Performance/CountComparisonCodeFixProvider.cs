using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using EfLint.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace EfLint.CodeFixes.Performance;

/// <summary>Replaces a Count() existence comparison with Any().</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CountComparisonCodeFixProvider)), Shared]
public sealed class CountComparisonCodeFixProvider : CodeFixProvider
{
    private const string EquivalenceKey = "EFL1002.UseAny";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(EfLintDiagnostics.CountComparisonId);

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
            if (!TryFindBinary(root, diagnostic.Location.SourceSpan, out _, out _, out _))
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Use Any() instead of comparing Count() with zero/one",
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
        if (root is null
            || !TryFindBinary(root, diagnostic.Location.SourceSpan, out var binary, out var invocation, out var awaited))
        {
            return document;
        }

        var oldName = invocation!.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name,
            SimpleNameSyntax simple => simple,
            _ => null,
        };
        if (oldName is null || !IsCountName(oldName.Identifier.ValueText))
        {
            return document;
        }

        // The analyzer records whether this is a sync Count (Any) or an awaited
        // CountAsync/LongCountAsync (AnyAsync) so the await is preserved correctly.
        diagnostic.Properties.TryGetValue(CountComparisonAnalyzer.ReplacementMethodProperty, out var replacementName);
        replacementName = string.IsNullOrEmpty(replacementName) ? "Any" : replacementName;
        var newName = SyntaxFactory.IdentifierName(replacementName!).WithTriviaFrom(oldName);
        ExpressionSyntax newExpression = invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.WithName(newName),
            _ => newName,
        };

        // Keep the existing argument list: it is empty for Count(), and for the
        // predicate overload Count(predicate) it must become Any(predicate).
        var renamedInvocation = invocation.WithExpression(newExpression);

        ExpressionSyntax core = renamedInvocation.WithoutTrivia();
        // The old fix replaced the whole comparison and silently removed `await`.
        ExpressionSyntax replacement = awaited ? SyntaxFactory.AwaitExpression(core) : core;
        replacement = replacement
            .WithLeadingTrivia(binary!.GetLeadingTrivia())
            .WithTrailingTrivia(binary.GetTrailingTrivia());

        return document.WithSyntaxRoot(root.ReplaceNode(binary, replacement));
    }

    private static bool TryFindBinary(
        SyntaxNode root,
        TextSpan span,
        out BinaryExpressionSyntax? binary,
        out InvocationExpressionSyntax? invocation,
        out bool awaited)
    {
        binary = null;
        invocation = null;
        awaited = false;

        var name = root.FindNode(span, getInnermostNodeForTie: true).FirstAncestorOrSelf<SimpleNameSyntax>();
        var foundInvocation = name?.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (foundInvocation is null || !IsCountName(name!.Identifier.ValueText))
        {
            return false;
        }

        ExpressionSyntax current = foundInvocation;
        while (true)
        {
            switch (current.Parent)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    current = parenthesized;
                    continue;
                case AwaitExpressionSyntax awaitExpression:
                    awaited = true;
                    current = awaitExpression;
                    continue;
                default:
                    break;
            }

            break;
        }

        if (current.Parent is not BinaryExpressionSyntax parentBinary)
        {
            return false;
        }

        binary = parentBinary;
        invocation = foundInvocation;
        return true;
    }

    private static bool IsCountName(string name) =>
        name is "Count" or "CountAsync" or "LongCount" or "LongCountAsync";
}
