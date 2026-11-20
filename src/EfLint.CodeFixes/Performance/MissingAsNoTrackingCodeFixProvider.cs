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

namespace EfLint.CodeFixes.Performance;

/// <summary>Inserts AsNoTracking() before a read-only materializing query call.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingAsNoTrackingCodeFixProvider)), Shared]
public sealed class MissingAsNoTrackingCodeFixProvider : CodeFixProvider
{
    private const string EquivalenceKey = "EFL1003.AddAsNoTracking";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(EfLintDiagnostics.MissingAsNoTrackingId);

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
            if (invocation?.Expression is not MemberAccessExpressionSyntax member
                || model.GetSymbolInfo(member.Expression).Symbol is INamedTypeSymbol
                || HasNoTrackingCall(member.Expression)
                || !CanApplyAsNoTracking(invocation, member.Expression, model))
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Use AsNoTracking for this read-only query",
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
        if (invocation?.Expression is not MemberAccessExpressionSyntax member
            || model.GetSymbolInfo(member.Expression).Symbol is INamedTypeSymbol
            || HasNoTrackingCall(member.Expression)
            || !CanApplyAsNoTracking(invocation, member.Expression, model))
        {
            return document;
        }

        var source = member.Expression;
        var asNoTracking = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    source.WithoutTrivia(),
                    SyntaxFactory.IdentifierName("AsNoTracking")))
            .WithTriviaFrom(source);

        var newExpression = member.WithExpression(asNoTracking);
        return document.WithSyntaxRoot(root.ReplaceNode(invocation, invocation.WithExpression(newExpression)));
    }

    private static InvocationExpressionSyntax? FindInvocation(SyntaxNode root, TextSpan span)
    {
        return root.FindNode(span, getInnermostNodeForTie: true)
            .FirstAncestorOrSelf<InvocationExpressionSyntax>();
    }

    /// <summary>
    /// Defensive guard: AsNoTracking only exists on IQueryable&lt;TEntity&gt;. Never offer a
    /// fix for a scalar aggregate or a non-entity element type (would produce CS0452).
    /// </summary>
    private static bool CanApplyAsNoTracking(
        InvocationExpressionSyntax invocation,
        ExpressionSyntax source,
        SemanticModel model)
    {
        var resultType = model.GetTypeInfo(invocation).Type;
        if (resultType is null || resultType.IsValueType || resultType.SpecialType == SpecialType.System_String)
        {
            return false;
        }

        var info = model.GetTypeInfo(source);
        var sourceType = info.Type ?? info.ConvertedType;
        if (sourceType is null)
        {
            return false;
        }

        var elementType = GetElementType(sourceType)
            ?? sourceType.AllInterfaces.Select(GetElementType).FirstOrDefault(type => type is not null);

        return elementType is INamedTypeSymbol named
            && named.TypeKind == TypeKind.Class
            && !named.IsAnonymousType
            && named.SpecialType == SpecialType.None;
    }

    private static ITypeSymbol? GetElementType(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named || !named.IsGenericType || named.TypeArguments.Length != 1)
        {
            return null;
        }

        var ns = named.ContainingNamespace?.ToDisplayString();
        return named.OriginalDefinition.Name switch
        {
            "IQueryable" or "IOrderedQueryable" when ns == "System.Linq" => named.TypeArguments[0],
            "IEnumerable" or "ICollection" or "IList" when ns == "System.Collections.Generic" => named.TypeArguments[0],
            "DbSet" when ns == "Microsoft.EntityFrameworkCore" => named.TypeArguments[0],
            _ => null,
        };
    }

    private static bool HasNoTrackingCall(ExpressionSyntax source)
    {
        foreach (var node in source.DescendantNodesAndSelf())
        {
            if (node is InvocationExpressionSyntax invocation
                && invocation.Expression is MemberAccessExpressionSyntax member
                && member.Name.Identifier.ValueText is "AsNoTracking"
                    or "AsNoTrackingWithIdentityResolution")
            {
                return true;
            }
        }

        return false;
    }
}
