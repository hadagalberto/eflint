using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace EfLint.Tests.TestInfrastructure;

public class LineEndingCoverageTests
{
    [Theory]
    [InlineData("a\r\nb\r\nc", "\n", "a\nb\nc")]
    [InlineData("a\nb\nc", "\r\n", "a\r\nb\r\nc")]
    [InlineData("a\r\nb\nc", "\r\n", "a\r\nb\r\nc")]
    public void WithLineEndings_NormalizesMixedSources(string input, string eol, string expected) =>
        Assert.Equal(expected, EfLintTestResources.WithLineEndings(input, eol));

    [Fact]
    public void EveryCodeFixTest_UsesTheBothLineEndingsHelper()
    {
        var repositoryRoot = DotNetRunner.FindRepositoryRoot();
        var testsDirectory = Path.Combine(repositoryRoot, "tests", "EfLint.Tests");
        var sources = Directory.GetFiles(testsDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsGenerated(path))
            .Where(path => !string.Equals(Path.GetFileName(path), nameof(LineEndingCoverageTests) + ".cs", StringComparison.Ordinal))
            .ToArray();

        var codeFixTests = sources
            .Where(path => Path.GetFileNameWithoutExtension(path).EndsWith("CodeFixTests", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(codeFixTests);

        var offenders = new List<string>();
        foreach (var source in sources)
        {
            var text = File.ReadAllText(source);
            var isHelper = string.Equals(Path.GetFileName(source), "EfLintTestResources.cs", StringComparison.Ordinal);
            if (!isHelper && text.Contains("CSharpCodeFixTest<", StringComparison.Ordinal))
            {
                offenders.Add($"{Path.GetFileName(source)} constructs CSharpCodeFixTest directly");
            }

            if (Path.GetFileNameWithoutExtension(source).EndsWith("CodeFixTests", StringComparison.Ordinal)
                && !text.Contains("EfLintTestResources.Verify", StringComparison.Ordinal))
            {
                offenders.Add($"{Path.GetFileName(source)} does not use EfLintTestResources.Verify* helper");
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    private static bool IsGenerated(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/obj/", StringComparison.Ordinal)
            || normalized.Contains("/bin/", StringComparison.Ordinal);
    }
}
