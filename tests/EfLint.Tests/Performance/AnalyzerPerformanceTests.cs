using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.Tests.TestInfrastructure;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace EfLint.Tests.Performance;

public class AnalyzerPerformanceTests
{
    [Fact]
    public async Task AnalyzeTwoThousandMethods_CompletesWithinBudget()
    {
        var source = new StringBuilder();
        source.AppendLine("public class Sample");
        source.AppendLine("{");
        for (var i = 0; i < 2000; i++)
        {
            source.AppendLine($"    public void M{i}(BlogContext db) {{ db.Blogs.FromSqlRaw(\"SELECT 1\"); }}");
        }

        source.AppendLine("}");

        var test = new CSharpAnalyzerTest<RawSqlInjectionAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = EfLintTestResources.EfCore,
            TestCode = EfLintTestResources.WithModel(source.ToString()),
        };

        var stopwatch = Stopwatch.StartNew();
        await test.RunAsync();
        stopwatch.Stop();

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(60),
            $"Analyzing 2,000 methods took {stopwatch.ElapsedMilliseconds} ms, above the 60 s budget.");
    }
}
