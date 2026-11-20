using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using EfLint.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace EfLint.Tests.Infrastructure;

public class EfLintOptionsTests
{
    [Fact]
    public void Provider_DefaultsToSqlServer()
    {
        var options = CreateOptions();

        Assert.Equal("sqlserver", options.Provider);
    }

    [Fact]
    public void Provider_ReadsEditorConfigValue()
    {
        var options = CreateOptions(("eflint.provider", "Postgres"));

        Assert.Equal("postgres", options.Provider);
    }

    [Fact]
    public void IsRuleEnabled_HonorsExplicitFalse()
    {
        var options = CreateOptions(("eflint.EFL3001.enabled", "false"));

        Assert.False(options.IsRuleEnabled("EFL3001"));
        Assert.True(options.IsRuleEnabled("EFL3002"));
    }

    [Fact]
    public void IsRuleEnabled_HonorsExplicitTrueAndIgnoresInvalidValue()
    {
        var options = CreateOptions(("eflint.EFL3002.enabled", "true"), ("eflint.EFL3001.enabled", "not-a-bool"));

        Assert.True(options.IsRuleEnabled("EFL3002"));
        Assert.True(options.IsRuleEnabled("EFL3001"));
    }

    private static EfLintOptions CreateOptions(params (string Key, string Value)[] values)
    {
        var dictionary = values.ToDictionary(pair => pair.Key, pair => pair.Value);
        var provider = new TestAnalyzerConfigOptionsProvider(dictionary);
        var analyzerOptions = new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, provider);
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("class C { }");

        return EfLintOptions.Create(analyzerOptions, tree);
    }

    private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly TestAnalyzerConfigOptions _options;

        public TestAnalyzerConfigOptionsProvider(IDictionary<string, string> values)
        {
            _options = new TestAnalyzerConfigOptions(values);
        }

        public override AnalyzerConfigOptions GlobalOptions => _options;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _options;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _options;
    }

    private sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly IDictionary<string, string> _values;

        public TestAnalyzerConfigOptions(IDictionary<string, string> values)
        {
            _values = values;
        }

        public override IEnumerable<string> Keys => _values.Keys;

        public override bool TryGetValue(string key, out string value)
        {
            return _values.TryGetValue(key, out value!);
        }
    }
}
