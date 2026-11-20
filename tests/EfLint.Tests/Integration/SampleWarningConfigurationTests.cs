using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using EfLint.Tests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace EfLint.Tests.Integration;

public class SampleWarningConfigurationTests
{
    [Fact]
    public void SampleProject_DemotesEveryDescriptorDiagnosticWhenWarningsAreErrors()
    {
        var repositoryRoot = DotNetRunner.FindRepositoryRoot();
        var projectPath = Path.Combine(repositoryRoot, "samples", "EfLint.Sample", "EfLint.Sample.csproj");
        Assert.True(File.Exists(projectPath), $"Sample project not found at {projectPath}");

        // Generate the expected list from the descriptors, not from a hand-maintained copy,
        // so adding a rule without updating the sample fails this test.
        var descriptorIds = typeof(EfLintDiagnostics)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(DiagnosticDescriptor))
            .Select(field => field.GetValue(null))
            .OfType<DiagnosticDescriptor>()
            .Select(descriptor => descriptor.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(descriptorIds);

        var project = XDocument.Load(projectPath);
        var warningsProperty = project.Descendants("WarningsNotAsErrors").Single();
        var notAsErrors = warningsProperty.Value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => part.StartsWith("EFL", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        var missing = descriptorIds.Where(id => !notAsErrors.Contains(id)).ToArray();
        Assert.True(
            missing.Length == 0,
            $"Sample must list every EFL id in WarningsNotAsErrors; missing: {string.Join(", ", missing)}");

        var treatWarningsAsErrors = project.Descendants("TreatWarningsAsErrors").Single().Value;
        Assert.Equal("false", treatWarningsAsErrors, StringComparer.OrdinalIgnoreCase);
    }
}
