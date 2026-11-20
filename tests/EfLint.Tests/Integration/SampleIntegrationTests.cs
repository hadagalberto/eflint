using System;
using System.Collections.Generic;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SampleBuild_ProducesExactlyTheExpectedDiagnostics(bool warningsAsErrors)
    {
        var repositoryRoot = DotNetRunner.FindRepositoryRoot();
        var project = Path.Combine(repositoryRoot, "samples", "EfLint.Sample", "EfLint.Sample.csproj");
        Assert.True(File.Exists(project), $"Sample project not found at {project}");

        var arguments = new List<string> { "build", project, "--nologo", "-v:m", "-t:Rebuild" };
        if (warningsAsErrors)
        {
            arguments.Add("-warnaserror");
        }

        var (exitCode, output) = await DotNetRunner.RunAsync(repositoryRoot, arguments.ToArray());

        Assert.True(exitCode == 0, $"Sample build failed:\n{output}");
        Assert.False(
            output.Contains("error EFL", StringComparison.OrdinalIgnoreCase),
            $"Intentional sample diagnostics must stay warnings, even with -warnaserror:\n{output}");

        // MSBuild repeats each compiler diagnostic when a project is rebuilt; compare the
        // distinct source diagnostics rather than duplicated log lines.
        var actual = DiagnosticLine.Matches(output)
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .Select(line => DiagnosticLine.Match(line).Groups[1].Value)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        // One violation for each of the 20 rules, plus a second EFL3001 case.
        var expected = new[]
        {
            "EFL1001", "EFL1002", "EFL1003", "EFL1004", "EFL1005",
            "EFL1006", "EFL1007", "EFL1008", "EFL1009", "EFL1010",
            "EFL2001", "EFL2002", "EFL2003", "EFL2004", "EFL2005",
            "EFL2006", "EFL2007", "EFL2008",
            "EFL3001", "EFL3001", "EFL3002",
        };

        Assert.Equal(expected, actual);
    }
}
