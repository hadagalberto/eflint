using Microsoft.CodeAnalysis;

namespace EfLint;

/// <summary>Diagnostic descriptors and identifiers owned by EfLint.</summary>
public static class EfLintDiagnostics
{
    public const string RawSqlInjectionId = "EFL3001";
    public const string FlattenedSqlInterpolationId = "EFL3002";

    public static readonly DiagnosticDescriptor RawSqlInjection = new(
        id: RawSqlInjectionId,
        title: "Raw SQL built from dynamic values",
        messageFormat: "SQL passed to '{0}' contains non-constant values; use '{1}' with a FormattableString instead",
        category: "Security",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "FromSqlRaw, ExecuteSqlRaw and SqlQueryRaw do not parameterize interpolated values. Building SQL this way allows SQL injection.",
        helpLinkUri: "https://github.com/hadagalberto/eflint/blob/main/docs/rules/EFL3001.md");

    public static readonly DiagnosticDescriptor FlattenedSqlInterpolation = new(
        id: FlattenedSqlInterpolationId,
        title: "Interpolated SQL flattened to a string",
        messageFormat: "Interpolated SQL assigned to a string loses parameter binding; pass it directly to '{1}' instead of '{0}'",
        category: "Security",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An interpolated string stored in a string variable is evaluated before the EF Core raw SQL API sees it, so its values are inlined into the SQL text.",
        helpLinkUri: "https://github.com/hadagalberto/eflint/blob/main/docs/rules/EFL3002.md");
}
