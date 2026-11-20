using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.CodeFixes.Security;
using EfLint.Tests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace EfLint.Tests.Security;

/// <summary>
/// Regression coverage for EFL3001 quote handling. Expected values are derived
/// from the EF Core contract: a FormattableString hole becomes a SQL parameter,
/// so the SQL quotes that surrounded the old C# interpolation must be removed.
/// Keeping them would parameterize the quote characters as part of the value.
/// </summary>
public class RawSqlQuoteFixTests
{
    [Fact]
    public async Task QuotedInterpolation_FromSqlRaw_SingleQuotesRemoved()
    {
        // Hand-derived: $"...Name = '{name}'" -> ...Name = {name}
        // The leading ' and trailing ' around the hole are SQL syntax, not data.
        await EfLintTestResources.VerifyCodeFixAsync<RawSqlInjectionAnalyzer, RawSqlToFormattableStringCodeFixProvider>(
            """
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    db.Blogs.FromSqlRaw({|EFL3001:$"SELECT * FROM Blogs WHERE Name = '{name}'"|});
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    db.Blogs.FromSql($"SELECT * FROM Blogs WHERE Name = {name}");
                }
            }
            """);
    }

    [Fact]
    public async Task QuotedInterpolation_ExecuteSqlRaw_DoubleQuotesRemoved()
    {
        // Hand-derived: the C# \" escape renders a SQL double quote; both the
        // opening and closing quote around the hole must disappear.
        await EfLintTestResources.VerifyCodeFixAsync<RawSqlInjectionAnalyzer, RawSqlToFormattableStringCodeFixProvider>(
            """
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    db.Database.ExecuteSqlRaw({|EFL3001:$"DELETE FROM Blogs WHERE Name = \"{name}\""|});
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    db.Database.ExecuteSql($"DELETE FROM Blogs WHERE Name = {name}");
                }
            }
            """);
    }

    [Fact]
    public async Task QuotedInterpolation_TwoHoles_BothQuotePairsRemoved()
    {
        // Hand-derived: each hole is independently wrapped in single quotes,
        // so both pairs are removed and two parameters are produced.
        await EfLintTestResources.VerifyCodeFixAsync<RawSqlInjectionAnalyzer, RawSqlToFormattableStringCodeFixProvider>(
            """
            public class Sample
            {
                public void M(BlogContext db, string name, string title)
                {
                    db.Blogs.FromSqlRaw({|EFL3001:$"SELECT * FROM Blogs WHERE Name = '{name}' AND Title = '{title}'"|});
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db, string name, string title)
                {
                    db.Blogs.FromSql($"SELECT * FROM Blogs WHERE Name = {name} AND Title = {title}");
                }
            }
            """);
    }

    [Fact]
    public async Task UnsafeQuoteShape_Asymmetric_NoCodeFixOffered()
    {
        // Only the opening quote is adjacent to the hole. There is no way to know
        // what the closing delimiter is, so offering a fix would guess at SQL.
        Assert.Equal(0, await CodeFixOfferProbe.CountAsync<RawSqlInjectionAnalyzer, RawSqlToFormattableStringCodeFixProvider>(
            """
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    db.Blogs.FromSqlRaw($"SELECT * FROM Blogs WHERE Name = '{name}");
                }
            }
            """,
            EfLintDiagnostics.RawSqlInjectionId));
    }

    [Fact]
    public async Task UnsafeQuoteShape_EscapedQuotes_NoCodeFixOffered()
    {
        // '' is an escaped SQL quote. Removing one quote from a '' run would
        // corrupt the surrounding literal, so no fix may be offered.
        Assert.Equal(0, await CodeFixOfferProbe.CountAsync<RawSqlInjectionAnalyzer, RawSqlToFormattableStringCodeFixProvider>(
            """
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    db.Blogs.FromSqlRaw($"SELECT * FROM Blogs WHERE Name = ''{name}''");
                }
            }
            """,
            EfLintDiagnostics.RawSqlInjectionId));
    }

    [Fact]
    public async Task UnsafeQuoteShape_QuoteNotAdjacentToHole_NoCodeFixOffered()
    {
        // The text immediately before the hole is 'x', not a quote. The trailing
        // quote belongs to a larger literal and cannot be paired with the hole.
        Assert.Equal(0, await CodeFixOfferProbe.CountAsync<RawSqlInjectionAnalyzer, RawSqlToFormattableStringCodeFixProvider>(
            """
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    db.Blogs.FromSqlRaw($"SELECT * FROM Blogs WHERE Name = 'x{name}'");
                }
            }
            """,
            EfLintDiagnostics.RawSqlInjectionId));
    }

    [Fact]
    public async Task UnsafeQuoteShape_MixedQuotedAndUnquotedHoles_NoCodeFixOffered()
    {
        // One hole is quoted and the other is not: there is no single quote-removal
        // policy that applies to both, so the code fix must stay away.
        Assert.Equal(0, await CodeFixOfferProbe.CountAsync<RawSqlInjectionAnalyzer, RawSqlToFormattableStringCodeFixProvider>(
            """
            public class Sample
            {
                public void M(BlogContext db, string name, int id)
                {
                    db.Blogs.FromSqlRaw($"SELECT * FROM Blogs WHERE Name = '{name}' AND Id = {id}");
                }
            }
            """,
            EfLintDiagnostics.RawSqlInjectionId));
    }

    [Fact]
    public async Task UnsafeConcatenation_EscapedQuotes_NoCodeFixOffered()
    {
        // Same safety rule for concatenations: a doubled quote in either literal
        // makes the hole quotation ambiguous.
        Assert.Equal(0, await CodeFixOfferProbe.CountAsync<RawSqlInjectionAnalyzer, RawSqlToFormattableStringCodeFixProvider>(
            """
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    db.Database.ExecuteSqlRaw("DELETE FROM Blogs WHERE Name = '''" + name + "'''");
                }
            }
            """,
            EfLintDiagnostics.RawSqlInjectionId));
    }
}

/// <summary>
/// Builds a real workspace/compilation, runs the analyzer and asks the code fix
/// provider to register actions. Used to assert that an unsafe SQL shape gets no
/// offered fix at all (rather than a silently wrong one).
/// </summary>
internal static class CodeFixOfferProbe
{
    private static readonly Lazy<Task<ImmutableArray<MetadataReference>>> References =
        new(() => EfLintTestResources.EfCore.ResolveAsync(LanguageNames.CSharp, CancellationToken.None),
            LazyThreadSafetyMode.ExecutionAndPublication);

    public static async Task<int> CountAsync<TAnalyzer, TCodeFix>(string code, string diagnosticId)
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TCodeFix : CodeFixProvider, new()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId("ProbeProject");

        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            "ProbeProject",
            "ProbeProject",
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
            metadataReferences: await References.Value.ConfigureAwait(false));

        workspace.AddProject(projectInfo);
        var document = workspace.AddDocument(
            projectId,
            "Probe.cs",
            SourceText.From(EfLintTestResources.WithModel(code)))!;
        var compilation = (await document.Project.GetCompilationAsync().ConfigureAwait(false))!;

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.True(errors.Length == 0, "Probe source must compile: " + string.Join("; ", errors.Select(e => e.ToString())));

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new TAnalyzer()))
            .GetAnalyzerDiagnosticsAsync(CancellationToken.None)
            .ConfigureAwait(false);
        var target = diagnostics.FirstOrDefault(d => d.Id == diagnosticId);
        Assert.NotNull(target);

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document,
            target!,
            (action, _) => actions.Add(action),
            CancellationToken.None);
        await new TCodeFix().RegisterCodeFixesAsync(context).ConfigureAwait(false);
        return actions.Count;
    }
}
