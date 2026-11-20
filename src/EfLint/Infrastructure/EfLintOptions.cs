using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EfLint.Infrastructure;

/// <summary>
/// Reads EfLint-specific .editorconfig options. Diagnostic severities are left
/// to Roslyn's <c>dotnet_diagnostic.EFLxxxx.severity</c> handling; this type
/// only covers rule toggles and provider-specific hints.
/// </summary>
internal sealed class EfLintOptions
{
    private readonly AnalyzerConfigOptions _options;

    private EfLintOptions(AnalyzerConfigOptions options)
    {
        _options = options;
    }

    public string Provider
    {
        get
        {
            return TryGet("eflint.provider", out var provider)
                ? provider.Trim().ToLowerInvariant()
                : "sqlserver";
        }
    }

    public static EfLintOptions Create(AnalyzerOptions analyzerOptions, SyntaxTree syntaxTree)
    {
        return new EfLintOptions(analyzerOptions.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree));
    }

    public bool IsRuleEnabled(string ruleId, bool defaultValue = true)
    {
        return TryGet($"eflint.{ruleId}.enabled", out var value)
            && bool.TryParse(value, out var enabled)
            ? enabled
            : defaultValue;
    }

    private bool TryGet(string key, out string value)
    {
        if (_options.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
        {
            value = raw;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
