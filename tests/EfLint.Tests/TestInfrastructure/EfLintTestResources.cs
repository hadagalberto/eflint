using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace EfLint.Tests.TestInfrastructure;

internal static class EfLintTestResources
{
    /// <summary>Every line ending a code fix must be able to preserve.</summary>
    private static readonly string[] LineEndings = { "\n", "\r\n" };

    public static readonly ReferenceAssemblies EfCore = ReferenceAssemblies.Net.Net80.AddPackages(
        ImmutableArray.Create(new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.EntityFrameworkCore.Relational", "8.0.11")));

    /// <summary>Minimal EF Core model shared by every analyzer test.</summary>
    public const string Model = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.Extensions.DependencyInjection;

        public class Blog
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }

        public class BlogContext : DbContext
        {
            public DbSet<Blog> Blogs => Set<Blog>();
        }

        """;

    public static string WithModel(string code) => Model + code;

    public static Task VerifyAnalyzerAsync<TAnalyzer>(string code)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = EfCore,
            TestCode = WithModel(code),
        };

        return test.RunAsync();
    }

    /// <summary>
    /// Runs a code-fix test twice, once with LF and once with CRLF sources. Code fixes must
    /// preserve the document's own line endings, so a fix that hard-codes either style fails
    /// one of the two runs.
    /// </summary>
    public static Task VerifyCodeFixAsync<TAnalyzer, TCodeFix>(string before, string after)
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TCodeFix : CodeFixProvider, new()
        => RunCodeFixAsync<TAnalyzer, TCodeFix>(WithModel(before), WithModel(after), fixAll: false);

    /// <summary>Fix All variant of <see cref="VerifyCodeFixAsync{TAnalyzer,TCodeFix}"/>.</summary>
    public static Task VerifyFixAllAsync<TAnalyzer, TCodeFix>(string before, string after)
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TCodeFix : CodeFixProvider, new()
        => RunCodeFixAsync<TAnalyzer, TCodeFix>(WithModel(before), WithModel(after), fixAll: true);

    /// <summary>
    /// Line-ending-agnostic code-fix verification for sources that already contain their own
    /// usings and model declarations (i.e. do not use <see cref="WithModel"/>).
    /// </summary>
    public static Task VerifyCodeFixRawAsync<TAnalyzer, TCodeFix>(string before, string after)
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TCodeFix : CodeFixProvider, new()
        => RunCodeFixAsync<TAnalyzer, TCodeFix>(before, after, fixAll: false);

    /// <summary>Fix All variant of <see cref="VerifyCodeFixRawAsync{TAnalyzer,TCodeFix}"/>.</summary>
    public static Task VerifyFixAllRawAsync<TAnalyzer, TCodeFix>(string before, string after)
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TCodeFix : CodeFixProvider, new()
        => RunCodeFixAsync<TAnalyzer, TCodeFix>(before, after, fixAll: true);

    /// <summary>Converts every line ending in <paramref name="text"/> to <paramref name="eol"/>.</summary>
    public static string WithLineEndings(string text, string eol) =>
        text.Replace("\r\n", "\n").Replace("\n", eol);

    private static async Task RunCodeFixAsync<TAnalyzer, TCodeFix>(string before, string after, bool fixAll)
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TCodeFix : CodeFixProvider, new()
    {
        foreach (var eol in LineEndings)
        {
            var test = new CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>
            {
                ReferenceAssemblies = EfCore,
                TestCode = WithLineEndings(before, eol),
                FixedCode = WithLineEndings(after, eol),
            };

            if (fixAll)
            {
                test.BatchFixedCode = WithLineEndings(after, eol);
                test.CodeActionValidationMode = CodeActionValidationMode.None;
                test.NumberOfFixAllIterations = 1;
                test.NumberOfFixAllInDocumentIterations = 1;
                test.NumberOfFixAllInProjectIterations = 1;
                test.NumberOfIncrementalIterations = 2;
            }

            await test.RunAsync().ConfigureAwait(false);
        }
    }
}
