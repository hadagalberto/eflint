using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using EfLint.Analyzers;

namespace EfLint.CodeFixes.Correctness;

/// <summary>Removes an Include call that is discarded by a later projection.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(IncludeWithProjectionCodeFixProvider)), Shared]
public sealed class IncludeWithProjectionCodeFixProvider : CodeFixProvider
{
    private const string EquivalenceKey = "EFL2006.RemoveInclude";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(EfLintDiagnostics.IncludeWithProjectionId);

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
            if (!diagnostic.Properties.TryGetValue(IncludeWithProjectionAnalyzer.FixableProperty, out var fixable)
                || fixable != "true")
            {
                continue;
            }

            var invocation = FindInvocation(root, diagnostic.Location.SourceSpan);
            if (invocation is null || GetSource(model, invocation) is null || HasIdentityProjection(invocation))
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Remove the ignored Include",
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
        var source = invocation is null ? null : GetSource(model, invocation);
        if (invocation is null || source is null || HasIdentityProjection(invocation))
        {
            return document;
        }

        var replacement = source.WithoutTrivia()
            .WithLeadingTrivia(invocation.GetLeadingTrivia())
            .WithTrailingTrivia(invocation.GetTrailingTrivia());

        return document.WithSyntaxRoot(root.ReplaceNode(invocation, replacement));
    }

    private static InvocationExpressionSyntax? FindInvocation(SyntaxNode root, TextSpan span)
    {
        return root.FindNode(span, getInnermostNodeForTie: true)
            .FirstAncestorOrSelf<InvocationExpressionSyntax>();
    }

    /// <summary>
    /// Defensive guard: removing an Include before an identity Select would change which
    /// navigations are populated, so no fix may be offered for that shape.
    /// </summary>
    private static bool HasIdentityProjection(InvocationExpressionSyntax include)
    {
        ExpressionSyntax current = include;
        while (current.Parent is MemberAccessExpressionSyntax member
            && member.Expression == current
            && member.Parent is InvocationExpressionSyntax outer
            && outer.Expression == member)
        {
            if (member.Name.Identifier.ValueText is "Select" or "SelectMany")
            {
                return outer.ArgumentList.Arguments.Any(argument =>
                    argument.Expression is LambdaExpressionSyntax lambda && IsIdentityLambda(lambda));
            }

            current = outer;
        }

        return false;
    }

    private static bool IsIdentityLambda(LambdaExpressionSyntax lambda)
    {
        var parameterName = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.ValueText,
            ParenthesizedLambdaExpressionSyntax parenthesized when parenthesized.ParameterList.Parameters.Count == 1
                => parenthesized.ParameterList.Parameters[0].Identifier.ValueText,
            _ => null,
        };

        if (parameterName is null)
        {
            return false;
        }

        ExpressionSyntax? body = lambda.Body switch
        {
            ParenthesizedExpressionSyntax parenthesized => parenthesized.Expression,
            CastExpressionSyntax cast => cast.Expression,
            ExpressionSyntax expression => expression,
            _ => null,
        };

        return body is IdentifierNameSyntax identifier
            && identifier.Identifier.ValueText == parameterName;
    }

    private static ExpressionSyntax? GetSource(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax member
            && model.GetSymbolInfo(member.Expression).Symbol is not INamedTypeSymbol)
        {
            return member.Expression;
        }

        return invocation.ArgumentList.Arguments.Count > 0
            ? invocation.ArgumentList.Arguments[0].Expression
            : null;
    }
}
