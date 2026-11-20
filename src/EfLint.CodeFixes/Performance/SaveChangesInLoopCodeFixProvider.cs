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

/// <summary>Moves a SaveChanges statement out of a loop and places it right after the loop.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SaveChangesInLoopCodeFixProvider)), Shared]
public sealed class SaveChangesInLoopCodeFixProvider : CodeFixProvider
{
    private const string EquivalenceKey = "EFL1007.MoveAfterLoop";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(EfLintDiagnostics.SaveChangesInLoopId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        var semanticModel = await context.Document
            .GetSemanticModelAsync(context.CancellationToken)
            .ConfigureAwait(false);

        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue(SaveChangesInLoopAnalyzer.FixableProperty, out var fixable)
                || fixable != "true")
            {
                continue;
            }

            var invocation = FindInvocation(root, diagnostic.Location.SourceSpan);
            if (invocation is null
                || invocation.Parent is not ExpressionStatementSyntax
                || SaveChangesInLoopAnalyzer.FindEnclosingLoopStatement(invocation) is null)
            {
                continue;
            }

            // The analyzer only checks the statement shape. A receiver declared
            // inside the loop would be out of scope for the moved statement
            // (CS0103), so such a fix must never be offered.
            if (semanticModel is null
                || !ReceiverRemainsInScope(invocation, semanticModel, context.CancellationToken))
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Move SaveChanges after the loop",
                    cancellationToken => FixAsync(context.Document, diagnostic, cancellationToken),
                    equivalenceKey: EquivalenceKey),
                diagnostic);
        }
    }

    /// <summary>
    /// Returns false when any identifier in the SaveChanges receiver resolves to
    /// a local declared inside the loop we are moving the statement out of.
    /// </summary>
    private static bool ReceiverRemainsInScope(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var loop = SaveChangesInLoopAnalyzer.FindEnclosingLoopStatement(invocation);
        if (loop is null)
        {
            return false;
        }

        var receiver = GetReceiver(invocation.Expression);
        if (receiver is null)
        {
            return false;
        }

        foreach (var identifier in receiver.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            if (semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol is ILocalSymbol local
                && IsDeclaredInsideLoop(local, loop, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private static ExpressionSyntax? GetReceiver(ExpressionSyntax expression) =>
        expression switch
        {
            MemberAccessExpressionSyntax member => member.Expression,
            MemberBindingExpressionSyntax binding when binding.Parent is ConditionalAccessExpressionSyntax conditional
                => conditional.Expression,
            _ => null,
        };

    private static bool IsDeclaredInsideLoop(
        ILocalSymbol local,
        StatementSyntax loop,
        CancellationToken cancellationToken)
    {
        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            if (loop.Span.Contains(reference.GetSyntax(cancellationToken).Span))
            {
                return true;
            }
        }

        return false;
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
        if (invocation is null || invocation.Parent is not ExpressionStatementSyntax saveStatement)
        {
            return document;
        }

        var loop = SaveChangesInLoopAnalyzer.FindEnclosingLoopStatement(invocation);
        if (loop is null)
        {
            return document;
        }

        var body = SaveChangesInLoopAnalyzer.GetLoopBody(loop);
        if (body is null)
        {
            return document;
        }

        StatementSyntax newLoop;
        if (body is BlockSyntax block)
        {
            if (!ReferenceEquals(saveStatement.Parent, block))
            {
                return document;
            }

            var newBlock = block.WithStatements(block.Statements.Remove(saveStatement));
            newLoop = loop.ReplaceNode(block, newBlock);
        }
        else if (ReferenceEquals(body, saveStatement))
        {
            // A single-statement loop body needs an empty block once the save is removed.
            var emptyBlock = SyntaxFactory.Block().WithLeadingTrivia(body.GetLeadingTrivia());
            newLoop = loop.ReplaceNode(body, (StatementSyntax)emptyBlock);
        }
        else
        {
            return document;
        }

        var movedSave = WithLoopIndentation(saveStatement, loop, newLoop);
        var newRoot = root.ReplaceNode(loop, new StatementSyntax[] { newLoop, movedSave });
        return document.WithSyntaxRoot(newRoot);
    }

    private static ExpressionStatementSyntax WithLoopIndentation(
        ExpressionStatementSyntax saveStatement,
        StatementSyntax loop,
        StatementSyntax newLoop)
    {
        var indentation = GetIndentation(loop);
        var leading = new List<SyntaxTrivia>();

        // The loop's closing brace normally carries the end-of-line trivia, but a
        // one-line loop may not. Reuse the document's own end-of-line in that case so
        // the file keeps its line endings (LF or CRLF).
        var endOfLine = saveStatement.GetTrailingTrivia()
            .FirstOrDefault(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));
        if (endOfLine.RawKind != 0
            && !newLoop.GetTrailingTrivia().Any(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia)))
        {
            leading.Add(endOfLine);
        }

        leading.AddRange(indentation);

        return saveStatement.WithoutLeadingTrivia()
            .WithoutTrailingTrivia()
            .WithLeadingTrivia(SyntaxFactory.TriviaList(leading))
            .WithTrailingTrivia(saveStatement.GetTrailingTrivia());
    }

    private static SyntaxTriviaList GetIndentation(SyntaxNode statement)
    {
        var trivia = statement.GetLeadingTrivia();
        for (var i = trivia.Count - 1; i >= 0; i--)
        {
            if (trivia[i].IsKind(SyntaxKind.WhitespaceTrivia))
            {
                return SyntaxFactory.TriviaList(trivia[i]);
            }
        }

        return SyntaxFactory.TriviaList();
    }

    private static InvocationExpressionSyntax? FindInvocation(SyntaxNode root, TextSpan span)
    {
        return root.FindNode(span, getInnermostNodeForTie: true)
            .FirstAncestorOrSelf<InvocationExpressionSyntax>();
    }
}
