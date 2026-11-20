using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Integration;

public class SampleIntegrationTests
{
    private static readonly Regex DiagnosticLine = new(
        @"Program\.cs\(\d+,\d+\):\s+warning\s+(EFL\d{4})\s*:",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public async Task SampleBuild_ProducesExactlyTheExpectedDiagnostics()
    {
        var repositoryRoot = DotNetRunner.FindRepositoryRoot();
        var project = Path.Combine(repositoryRoot, "samples", "EfLint.Sample", "EfLint.Sample.csproj");
        Assert.True(File.Exists(project), $"Sample project not found at {project}");

        var (exitCode, output) = await DotNetRunner.RunAsync(
            repositoryRoot,
            "build",
            project,
            "--nologo",
            "-v:m",
            "-t:Rebuild");

        Assert.True(exitCode == 0, $"Sample build failed:\n{output}");

        // MSBuild repeats each compiler diagnostic when a project is rebuilt; compare the
        // distinct source diagnostics rather than duplicated log lines.
        var actual = DiagnosticLine.Matches(output)
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .Select(line => DiagnosticLine.Match(line).Groups[1].Value)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var expected = new[] { "EFL3001", "EFL3001", "EFL3002" };

        Assert.Equal(expected, actual);
    }
}
