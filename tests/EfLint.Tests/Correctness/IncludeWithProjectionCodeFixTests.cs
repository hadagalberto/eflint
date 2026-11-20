using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.CodeFixes.Correctness;
using EfLint.Tests.TestInfrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace EfLint.Tests.Correctness;

public class IncludeWithProjectionCodeFixTests
{
    private const string ModelExtension = """
        public class Post
        {
            public int Id { get; set; }
            public int BlogId { get; set; }
        }

        public class BlogWithPosts
        {
            public int Id { get; set; }
            public List<Post> Posts { get; set; } = new();
        }

        public class BloggingContext : DbContext
        {
            public DbSet<BlogWithPosts> Blogs => Set<BlogWithPosts>();
        }

        """;

    [Fact]
    public async Task SimpleInclude_Removed()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var ids = db.Blogs.{|EFL2006:Include|}(b => b.Posts).Select(b => b.Id).ToList();
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var ids = db.Blogs.Select(b => b.Id).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task IncludeAfterWhere_Removed()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var ids = db.Blogs.Where(b => b.Id > 0).{|EFL2006:Include|}(b => b.Posts).Select(b => b.Id).ToList();
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var ids = db.Blogs.Where(b => b.Id > 0).Select(b => b.Id).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task StringInclude_Removed()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var ids = db.Blogs.{|EFL2006:Include|}("Posts").Select(b => b.Id).ToList();
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var ids = db.Blogs.Select(b => b.Id).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task StaticInvocation_Removed()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var ids = EntityFrameworkQueryableExtensions.{|EFL2006:Include|}(db.Blogs, b => b.Posts).Select(b => b.Id).ToList();
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var ids = db.Blogs.Select(b => b.Id).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task IncludeBeforeSelectMany_Removed()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var posts = db.Blogs.{|EFL2006:Include|}(b => b.Posts).SelectMany(b => b.Posts).ToList();
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var posts = db.Blogs.SelectMany(b => b.Posts).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task FixAll_MultipleIncludes_Removed()
    {
        await EfLintTestResources.VerifyFixAllAsync<IncludeWithProjectionAnalyzer, IncludeWithProjectionCodeFixProvider>(
            ModelExtension + """
                public class Sample
                {
                    public void M(BloggingContext db)
                    {
                        var a = db.Blogs.{|EFL2006:Include|}(b => b.Posts).Select(b => b.Id).ToList();
                        var c = db.Blogs.{|EFL2006:Include|}(b => b.Posts).Select(b => b.Id).ToList();
                    }
                }
                """,
            ModelExtension + """
                public class Sample
                {
                    public void M(BloggingContext db)
                    {
                        var a = db.Blogs.Select(b => b.Id).ToList();
                        var c = db.Blogs.Select(b => b.Id).ToList();
                    }
                }
                """);
    }

    // Defensive guard: even a stale diagnostic with Fixable=true must not remove an Include
    // whose Select keeps the entity in the result.
    [Fact]
    public async Task IdentityProjection_CodeFixNotOffered()
    {
        Assert.Equal(0, await CountActionsAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var blogs = db.Blogs.Include(b => b.Posts).Select(b => b).ToList();
                }
            }
            """));
    }

    private static async Task<int> CountActionsAsync(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(ModelExtension + code);
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

        var include = tree.GetRoot().DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .First(node => node.Expression is MemberAccessExpressionSyntax member
                && member.Name.Identifier.ValueText == "Include");
        var location = ((MemberAccessExpressionSyntax)include.Expression).Name.GetLocation();
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(IncludeWithProjectionAnalyzer.FixableProperty, "true");
        var diagnostic = Diagnostic.Create(
            EfLintDiagnostics.IncludeWithProjection,
            location,
            properties);

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None);
        await new IncludeWithProjectionCodeFixProvider().RegisterCodeFixesAsync(context).ConfigureAwait(false);
        return actions.Count;
    }

    private static Task VerifyAsync(string before, string after) =>
        EfLintTestResources.VerifyCodeFixAsync<IncludeWithProjectionAnalyzer, IncludeWithProjectionCodeFixProvider>(
            ModelExtension + before,
            ModelExtension + after);
}
