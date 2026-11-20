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

        if (!TryBuildFixedContents(argument.Expression, out var contents))
        {
            return document;
        }

        var newSql = SyntaxFactory.InterpolatedStringExpression(
                SyntaxFactory.Token(SyntaxKind.InterpolatedStringStartToken),
                contents,
                SyntaxFactory.Token(SyntaxKind.InterpolatedStringEndToken))
            .WithTriviaFrom(argument.Expression);
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
            // ElasticLineFeed always materialises as LF, which corrupts documents that use
            // CRLF. Read the newline actually used by the document (falling back to the
            // workspace formatting option) so the inserted using preserves its EOL style.
            var newLine = GetDocumentNewLine(document, root);
            var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(EntityFrameworkCoreNamespace))
                .WithTrailingTrivia(SyntaxFactory.EndOfLine(newLine));
            newRoot = compilationUnit.AddUsings(usingDirective);
        }

        return document.WithSyntaxRoot(newRoot);
    }

    private static string GetDocumentNewLine(Document document, SyntaxNode root)
    {
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                return trivia.ToString();
            }
        }

        // No line break anywhere in the document: defer to the workspace formatting option.
        return document.Project.Solution.Options
            .GetOption(Microsoft.CodeAnalysis.Formatting.FormattingOptions.NewLine, LanguageNames.CSharp)
            ?? "\n";
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

    /// <summary>
    /// True only when the SQL text can be converted without changing what the
    /// parameters mean. A quoted interpolation hole must have its SQL quotes
    /// removed (otherwise the quotes become part of the parameter value), and an
    /// ambiguous quote shape must suppress the fix entirely.
    /// </summary>
    private static bool CanConvert(ExpressionSyntax expression) =>
        TryBuildFixedContents(expression, out _);

    private static bool TryBuildFixedContents(
        ExpressionSyntax expression,
        out SyntaxList<InterpolatedStringContentSyntax> contents)
    {
        contents = default;
        var segments = new List<SqlSegment>();
        var unwrapped = Unparen(expression);

        if (unwrapped is InterpolatedStringExpressionSyntax interpolated)
        {
            foreach (var content in interpolated.Contents)
            {
                switch (content)
                {
                    case InterpolatedStringTextSyntax text:
                        segments.Add(SqlSegment.ForText(text.TextToken.ValueText ?? string.Empty, text));
                        break;
                    case InterpolationSyntax hole:
                        segments.Add(SqlSegment.ForHole(hole));
                        break;
                    default:
                        return false;
                }
            }
        }
        else if (IsStringConcatenation(unwrapped))
        {
            var parts = new List<ExpressionSyntax>();
            FlattenConcatenation(unwrapped, parts);
            foreach (var part in parts)
            {
                if (part is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    segments.Add(SqlSegment.ForText(literal.Token.ValueText ?? string.Empty, literal));
                }
                else
                {
                    segments.Add(SqlSegment.ForHole(part.WithoutTrivia()));
                }
            }
        }
        else
        {
            return false;
        }

        if (!TryPlanQuoteRemoval(segments))
        {
            return false;
        }

        var result = new List<InterpolatedStringContentSyntax>(segments.Count);
        foreach (var segment in segments)
        {
            if (segment.IsHole)
            {
                result.Add(segment.Source is InterpolationSyntax interpolation
                    ? interpolation
                    : SyntaxFactory.Interpolation((ExpressionSyntax)segment.Source));
                continue;
            }

            var value = segment.Text;
            if (segment.TrimStart && value.Length > 0)
            {
                value = value.Substring(1);
            }

            if (segment.TrimEnd && value.Length > 0)
            {
                value = value.Substring(0, value.Length - 1);
            }

            if (value.Length == 0)
            {
                continue;
            }

            var escaped = EscapeInterpolatedText(value);
            var token = SyntaxFactory.Token(
                SyntaxTriviaList.Empty,
                SyntaxKind.InterpolatedStringTextToken,
                escaped,
                value,
                SyntaxTriviaList.Empty);
            result.Add(SyntaxFactory.InterpolatedStringText(token));
        }

        contents = SyntaxFactory.List(result);
        return true;
    }

    /// <summary>
    /// Decides, for every interpolation hole, whether the SQL text immediately
    /// around it is a single unescaped quote pair, no quote at all, or something
    /// ambiguous. Mixed policies and ambiguous shapes suppress the fix.
    /// </summary>
    private static bool TryPlanQuoteRemoval(List<SqlSegment> segments)
    {
        var quotedCount = 0;
        var unquotedCount = 0;

        for (var i = 0; i < segments.Count; i++)
        {
            if (!segments[i].IsHole)
            {
                continue;
            }

            var before = i > 0 && !segments[i - 1].IsHole ? segments[i - 1].Text : string.Empty;
            var after = i + 1 < segments.Count && !segments[i + 1].IsHole ? segments[i + 1].Text : string.Empty;

            var beforeQuote = GetAdjacentQuote(before, fromEnd: true);
            var afterQuote = GetAdjacentQuote(after, fromEnd: false);

            if (beforeQuote.Kind == AdjacentQuoteKind.Ambiguous
                || afterQuote.Kind == AdjacentQuoteKind.Ambiguous)
            {
                // An escaped quote run cannot be reduced without changing the SQL.
                return false;
            }

            if (beforeQuote.Kind == AdjacentQuoteKind.None && afterQuote.Kind == AdjacentQuoteKind.None)
            {
                unquotedCount++;
                continue;
            }

            if (beforeQuote.Kind == AdjacentQuoteKind.None
                || afterQuote.Kind == AdjacentQuoteKind.None
                || beforeQuote.Quote != afterQuote.Quote)
            {
                return false;
            }

            segments[i - 1].TrimEnd = true;
            segments[i + 1].TrimStart = true;
            quotedCount++;
        }

        // Quoting one hole but not another has no single correct transformation.
        return quotedCount == 0 || unquotedCount == 0;
    }

    private enum AdjacentQuoteKind
    {
        None,
        Single,
        Ambiguous,
    }

    /// <summary>
    /// Returns the quote character at the edge of <paramref name="text"/> only
    /// when it is a single (unescaped) quote. A run of two or more identical
    /// quotes is an escaped SQL quote and makes the whole fix unsafe.
    /// </summary>
    private static (AdjacentQuoteKind Kind, char Quote) GetAdjacentQuote(string text, bool fromEnd)
    {
        if (text.Length == 0)
        {
            return (AdjacentQuoteKind.None, '\0');
        }

        var index = fromEnd ? text.Length - 1 : 0;
        var quote = text[index];
        if (quote != '\'' && quote != '"')
        {
            return (AdjacentQuoteKind.None, '\0');
        }

        var step = fromEnd ? -1 : 1;
        var count = 0;
        for (var i = index; i >= 0 && i < text.Length; i += step)
        {
            if (text[i] != quote)
            {
                break;
            }

            count++;
            if (count > 1)
            {
                return (AdjacentQuoteKind.Ambiguous, '\0');
            }
        }

        return (AdjacentQuoteKind.Single, quote);
    }

    private static string EscapeInterpolatedText(string value)
    {
        if (value.Length == 0)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '{': builder.Append("{{"); break;
                case '}': builder.Append("}}"); break;
                case '\r': builder.Append("\\r"); break;
                case '\n': builder.Append("\\n"); break;
                case '\t': builder.Append("\\t"); break;
                case '\0': builder.Append("\\0"); break;
                default: builder.Append(c); break;
            }
        }

        return builder.ToString();
    }

    private sealed class SqlSegment
    {
        private SqlSegment(bool isHole, string text, SyntaxNode source)
        {
            IsHole = isHole;
            Text = text;
            Source = source;
        }

        public bool IsHole { get; }
        public string Text { get; }
        public SyntaxNode Source { get; }
        public bool TrimStart { get; set; }
        public bool TrimEnd { get; set; }

        public static SqlSegment ForText(string text, SyntaxNode source) => new(false, text, source);
        public static SqlSegment ForHole(SyntaxNode source) => new(true, string.Empty, source);
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
