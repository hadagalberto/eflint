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

namespace EfLint.CodeFixes.Security;

/// <summary>
/// Converts a raw SQL call whose SQL text is an interpolated string (or a
/// string concatenation) into the equivalent FormattableString overload.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RawSqlToFormattableStringCodeFixProvider)), Shared]
public sealed class RawSqlToFormattableStringCodeFixProvider : CodeFixProvider
{
    private const string EntityFrameworkCoreNamespace = "Microsoft.EntityFrameworkCore";
    private const string FixAllEquivalenceKey = "EFL3001.UseFormattableString";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(EfLintDiagnostics.RawSqlInjectionId);

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
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (invocation is null)
            {
                continue;
            }

            var sqlArgument = FindSqlArgument(invocation, diagnostic.Location.SourceSpan);
            if (sqlArgument is null
                || !TryGetMethodName(invocation.Expression, out var rawName)
                || !RawSqlMethodNames.TryGetSafeName(rawName, out var safeName)
                || safeName is null
                || !CanConvert(sqlArgument.Expression))
            {
                continue;
            }

            var title = $"Use {safeName} with a FormattableString";
            context.RegisterCodeFix(
                CodeAction.Create(
                    title,
                    cancellationToken => FixAsync(context.Document, sqlArgument.SpanStart, safeName, cancellationToken),
                    equivalenceKey: FixAllEquivalenceKey),
                diagnostic);
        }
    }

    private static async Task<Document> FixAsync(
        Document document,
        int sqlArgumentPosition,
        string safeName,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var positionSpan = new TextSpan(sqlArgumentPosition, 0);
        var currentInvocation = root.FindNode(positionSpan).FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (currentInvocation is null)
        {
            return document;
        }

        var argument = FindSqlArgument(currentInvocation, positionSpan);
        if (argument is null)
        {
            return document;
        }

        var newSql = BuildInterpolatedString(argument.Expression).WithTriviaFrom(argument.Expression);
        var invocationWithSql = currentInvocation.ReplaceNode(argument.Expression, newSql);

        var methodExpression = Unparen(invocationWithSql.Expression);
        InvocationExpressionSyntax result;
        switch (methodExpression)
        {
            case MemberAccessExpressionSyntax memberAccess:
                SimpleNameSyntax newMemberName = memberAccess.Name is GenericNameSyntax genericMemberName
                    ? genericMemberName.WithIdentifier(
                        SyntaxFactory.Identifier(safeName).WithTriviaFrom(genericMemberName.Identifier))
                    : SyntaxFactory.IdentifierName(safeName).WithTriviaFrom(memberAccess.Name);
                result = invocationWithSql.ReplaceNode(memberAccess, memberAccess.WithName(newMemberName));
                break;

            case SimpleNameSyntax simpleName:
                SimpleNameSyntax newName = simpleName is GenericNameSyntax genericName
                    ? genericName.WithIdentifier(SyntaxFactory.Identifier(safeName).WithTriviaFrom(genericName.Identifier))
                    : SyntaxFactory.IdentifierName(safeName).WithTriviaFrom(simpleName);
                result = invocationWithSql.ReplaceNode(simpleName, newName);
                break;

            default:
                return document;
        }

        SyntaxNode newRoot = root.ReplaceNode(currentInvocation, result);
        if (newRoot is CompilationUnitSyntax compilationUnit && !HasEntityFrameworkCoreUsing(compilationUnit))
        {
            newRoot = compilationUnit.AddUsings(
                SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(EntityFrameworkCoreNamespace))
                    .WithTrailingTrivia(SyntaxFactory.ElasticLineFeed));
        }

        return document.WithSyntaxRoot(newRoot);
    }

    private static ArgumentSyntax? FindSqlArgument(InvocationExpressionSyntax invocation, TextSpan span)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.Span.Contains(span))
            {
                return argument;
            }
        }

        return invocation.ArgumentList.Arguments.Count > 0 ? invocation.ArgumentList.Arguments[0] : null;
    }

    private static bool CanConvert(ExpressionSyntax expression)
    {
        var unwrapped = Unparen(expression);
        return unwrapped is InterpolatedStringExpressionSyntax || IsStringConcatenation(unwrapped);
    }

    private static InterpolatedStringExpressionSyntax BuildInterpolatedString(ExpressionSyntax expression)
    {
        var unwrapped = Unparen(expression);
        if (unwrapped is InterpolatedStringExpressionSyntax existing)
        {
            return existing;
        }

        var parts = new List<ExpressionSyntax>();
        FlattenConcatenation(unwrapped, parts);

        var contents = new List<InterpolatedStringContentSyntax>(parts.Count);
        foreach (var part in parts)
        {
            if (part is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                var value = literal.Token.ValueText ?? string.Empty;
                var escaped = value.Replace("{", "{{").Replace("}", "}}");
                var token = SyntaxFactory.Token(
                    SyntaxTriviaList.Empty,
                    SyntaxKind.InterpolatedStringTextToken,
                    escaped,
                    value,
                    SyntaxTriviaList.Empty);
                contents.Add(SyntaxFactory.InterpolatedStringText(token));
            }
            else
            {
                contents.Add(SyntaxFactory.Interpolation(part.WithoutTrivia()));
            }
        }

        return SyntaxFactory.InterpolatedStringExpression(
            SyntaxFactory.Token(SyntaxKind.InterpolatedStringStartToken),
            SyntaxFactory.List(contents),
            SyntaxFactory.Token(SyntaxKind.InterpolatedStringEndToken));
    }

    private static void FlattenConcatenation(ExpressionSyntax expression, List<ExpressionSyntax> parts)
    {
        var unwrapped = Unparen(expression);
        if (unwrapped is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression))
        {
            FlattenConcatenation(binary.Left, parts);
            FlattenConcatenation(binary.Right, parts);
        }
        else
        {
            parts.Add(unwrapped);
        }
    }

    private static bool IsStringConcatenation(ExpressionSyntax expression)
    {
        var unwrapped = Unparen(expression);
        return unwrapped is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression);
    }

    private static ExpressionSyntax Unparen(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    private static bool TryGetMethodName(ExpressionSyntax expression, out string methodName)
    {
        switch (Unparen(expression))
        {
            case MemberAccessExpressionSyntax memberAccess:
                methodName = memberAccess.Name.Identifier.ValueText;
                return methodName.Length > 0;

            case SimpleNameSyntax simpleName:
                methodName = simpleName.Identifier.ValueText;
                return methodName.Length > 0;

            default:
                methodName = string.Empty;
                return false;
        }
    }

    private static bool HasEntityFrameworkCoreUsing(CompilationUnitSyntax root)
    {
        return root.DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Any(directive => directive.Name?.ToString() == EntityFrameworkCoreNamespace
                || directive.Name?.ToString() == "global::" + EntityFrameworkCoreNamespace);
    }
}
