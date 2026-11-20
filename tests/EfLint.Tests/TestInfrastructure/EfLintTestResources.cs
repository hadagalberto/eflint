using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace EfLint.Tests.TestInfrastructure;

internal static class EfLintTestResources
{
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

    public static Task VerifyCodeFixAsync<TAnalyzer, TCodeFix>(string before, string after)
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TCodeFix : CodeFixProvider, new()
    {
        var test = new CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>
        {
            ReferenceAssemblies = EfCore,
            TestCode = WithModel(before),
            FixedCode = WithModel(after),
        };

        return test.RunAsync();
    }

    public static Task VerifyFixAllAsync<TAnalyzer, TCodeFix>(string before, string after)
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TCodeFix : CodeFixProvider, new()
    {
        var test = new CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>
        {
            ReferenceAssemblies = EfCore,
            TestCode = WithModel(before),
            FixedCode = WithModel(after),
            BatchFixedCode = WithModel(after),
            CodeActionValidationMode = CodeActionValidationMode.None,
            NumberOfFixAllIterations = 1,
            NumberOfFixAllInDocumentIterations = 1,
            NumberOfFixAllInProjectIterations = 1,
            NumberOfIncrementalIterations = 2,
        };

        return test.RunAsync();
    }
}
