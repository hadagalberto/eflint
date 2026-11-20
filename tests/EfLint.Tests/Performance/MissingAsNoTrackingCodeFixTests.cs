using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.CodeFixes.Performance;
using EfLint.Tests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace EfLint.Tests.Performance;

public class MissingAsNoTrackingCodeFixTests
{
    [Fact]
    public async Task ToList_AsNoTrackingInserted()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var list = db.Blogs.{|EFL1003:ToList|}();
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var list = db.Blogs.AsNoTracking().ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task First_AsNoTrackingInserted()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var b = db.Blogs.{|EFL1003:First|}();
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var b = db.Blogs.AsNoTracking().First();
                }
            }
            """);
    }

    // Corrected: Count() yields a scalar, so AsNoTracking() is not applicable and the
    // analyzer must not produce a diagnostic to fix (review defect 1).
    [Fact]
    public async Task Count_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<MissingAsNoTrackingAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var c = db.Blogs.Count();
                }
            }
            """);
    }

    // Corrected: the previous scalar projection (x => x.Name) produced IQueryable<string>,
    // where AsNoTracking() does not compile (CS0452). Use an entity-valued projection.
    [Fact]
    public async Task SelectThenToList_AsNoTrackingInserted()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Select(x => x).{|EFL1003:ToList|}();
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Select(x => x).AsNoTracking().ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task WhereThenFirstOrDefault_AsNoTrackingInserted()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var b = db.Blogs.Where(x => x.Id > 0).{|EFL1003:FirstOrDefault|}();
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var b = db.Blogs.Where(x => x.Id > 0).AsNoTracking().FirstOrDefault();
                }
            }
            """);
    }

    [Fact]
    public async Task ToArray_AsNoTrackingInserted()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var arr = db.Blogs.{|EFL1003:ToArray|}();
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var arr = db.Blogs.AsNoTracking().ToArray();
                }
            }
            """);
    }

    [Fact]
    public async Task FixAll_MultipleQueries_AsNoTrackingInserted()
    {
        await EfLintTestResources.VerifyFixAllAsync<MissingAsNoTrackingAnalyzer, MissingAsNoTrackingCodeFixProvider>(
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var a = db.Blogs.{|EFL1003:ToList|}();
                    var c = db.Blogs.Where(x => x.Id > 0).{|EFL1003:ToArray|}();
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var a = db.Blogs.AsNoTracking().ToList();
                    var c = db.Blogs.Where(x => x.Id > 0).AsNoTracking().ToArray();
                }
            }
            """);
    }

    // Defensive code-fix guard: even if a caller hands the provider a diagnostic on a
    // non-entity query, it must not offer a fix that would not compile.
    [Fact]
    public async Task NonEntityQuery_CodeFixNotOffered()
    {
        Assert.Equal(0, await CountActionsAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var c = db.Blogs.Count();
                }
            }
            """, "Count"));

        Assert.Equal(0, await CountActionsAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var names = db.Blogs.Select(x => x.Name).ToList();
                }
            }
            """, "ToList"));
    }

    private static async Task<int> CountActionsAsync(string code, string marker)
    {
        var tree = CSharpSyntaxTree.ParseText(EfLintTestResources.WithModel(code));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "CodeFixGuardTest",
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Default,
            "CodeFixGuardTest",
            "CodeFixGuardTest",
            LanguageNames.CSharp,
            compilationOptions: compilation.Options,
            parseOptions: tree.Options,
            metadataReferences: references));
        var document = workspace.AddDocument(project.Id, "Test.cs", tree.GetText());

        var invocation = tree.GetRoot().DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .First(node => node.Expression is MemberAccessExpressionSyntax member
                && member.Name.Identifier.ValueText == marker);
        var location = ((MemberAccessExpressionSyntax)invocation.Expression).Name.GetLocation();
        var diagnostic = Diagnostic.Create(EfLintDiagnostics.MissingAsNoTracking, location);

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None);
        await new MissingAsNoTrackingCodeFixProvider().RegisterCodeFixesAsync(context).ConfigureAwait(false);
        return actions.Count;
    }

    private static Task VerifyAsync(string before, string after) =>
        EfLintTestResources.VerifyCodeFixAsync<MissingAsNoTrackingAnalyzer, MissingAsNoTrackingCodeFixProvider>(
            before,
            after);
}
