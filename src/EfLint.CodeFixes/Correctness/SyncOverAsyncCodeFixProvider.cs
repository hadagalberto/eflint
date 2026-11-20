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

/// <summary>Replaces a synchronous EF call with its awaited asynchronous overload.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SyncOverAsyncCodeFixProvider)), Shared]
public sealed class SyncOverAsyncCodeFixProvider : CodeFixProvider
{
    private const string EquivalenceKey = "EFL2001.UseAsync";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(EfLintDiagnostics.SyncOverAsyncId);

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
                || !diagnostic.Properties.TryGetValue(SyncOverAsyncAnalyzer.AsyncNameProperty, out var asyncName)
                || asyncName is not { Length: > 0 })
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Use {asyncName} with await",
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
            || !diagnostic.Properties.TryGetValue(SyncOverAsyncAnalyzer.AsyncNameProperty, out var asyncName)
            || asyncName is not { Length: > 0 })
        {
            return document;
        }

        diagnostic.Properties.TryGetValue(SyncOverAsyncAnalyzer.CancellationTokenProperty, out var token);
        var isFind = diagnostic.Properties.TryGetValue(SyncOverAsyncAnalyzer.IsFindProperty, out var find)
            && find == "true";

        diagnostic.Properties.TryGetValue(SyncOverAsyncAnalyzer.EntityTypeProperty, out var entityType);
        var newName = isFind && entityType is { Length: > 0 }
            ? asyncName + "<" + entityType + ">"
            : asyncName;

        var renamed = invocation.WithExpression(Rename(invocation.Expression, newName))
            .WithArgumentList(BuildArguments(invocation, token, isFind));

        var leading = invocation.GetLeadingTrivia();
        var trailing = invocation.GetTrailingTrivia();
        var awaitExpression = SyntaxFactory.AwaitExpression(renamed.WithoutTrivia());
        ExpressionSyntax awaited = NeedsParentheses(invocation)
            ? SyntaxFactory.ParenthesizedExpression(awaitExpression)
            : awaitExpression;
        awaited = awaited.WithLeadingTrivia(leading).WithTrailingTrivia(trailing);

        return document.WithSyntaxRoot(root.ReplaceNode(invocation, awaited));
    }

    private static bool NeedsParentheses(InvocationExpressionSyntax invocation)
    {
        return invocation.Parent is not (
            ExpressionStatementSyntax or EqualsValueClauseSyntax or ReturnStatementSyntax
            or ArrowExpressionClauseSyntax or ArgumentSyntax);
    }

    private static InvocationExpressionSyntax? FindInvocation(SyntaxNode root, TextSpan span)
    {
        return root.FindNode(span, getInnermostNodeForTie: true)
            .FirstAncestorOrSelf<InvocationExpressionSyntax>();
    }

    private static ExpressionSyntax Rename(ExpressionSyntax expression, string name)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax member =>
                member.WithName((SimpleNameSyntax)SyntaxFactory.ParseName(name).WithTriviaFrom(member.Name)),
            SimpleNameSyntax simple => (SimpleNameSyntax)SyntaxFactory.ParseName(name).WithTriviaFrom(simple),
            _ => expression,
        };
    }

    private static ArgumentListSyntax BuildArguments(InvocationExpressionSyntax invocation, string? token, bool isFind)
    {
        var original = invocation.ArgumentList.Arguments;
        var arguments = new List<ArgumentSyntax>();

        if (isFind)
        {
            var values = SyntaxFactory.SeparatedList(
                original.Select(argument => argument.Expression.WithoutTrivia()));
            var initializer = SyntaxFactory.InitializerExpression(SyntaxKind.ArrayInitializerExpression, values);
            var arrayType = SyntaxFactory.ArrayType(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.ObjectKeyword)),
                SyntaxFactory.SingletonList(SyntaxFactory.ArrayRankSpecifier(
                    SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                        SyntaxFactory.OmittedArraySizeExpression()))));
            arguments.Add(SyntaxFactory.Argument(SyntaxFactory.ArrayCreationExpression(arrayType, initializer)));
        }
        else
        {
            arguments.AddRange(original);
        }

        if (token is { Length: > 0 })
        {
            arguments.Add(SyntaxFactory.Argument(SyntaxFactory.IdentifierName(token)));
        }

        return invocation.ArgumentList.WithArguments(SyntaxFactory.SeparatedList(arguments));
    }
}
