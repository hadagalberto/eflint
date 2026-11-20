using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.CodeFixes.Correctness;
using EfLint.Tests.TestInfrastructure;
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
        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<
            IncludeWithProjectionAnalyzer, IncludeWithProjectionCodeFixProvider, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            ReferenceAssemblies = EfLintTestResources.EfCore,
            TestCode = EfLintTestResources.WithModel(ModelExtension + """
                public class Sample
                {
                    public void M(BloggingContext db)
                    {
                        var a = db.Blogs.{|EFL2006:Include|}(b => b.Posts).Select(b => b.Id).ToList();
                        var c = db.Blogs.{|EFL2006:Include|}(b => b.Posts).Select(b => b.Id).ToList();
                    }
                }
                """),
            FixedCode = EfLintTestResources.WithModel(ModelExtension + """
                public class Sample
                {
                    public void M(BloggingContext db)
                    {
                        var a = db.Blogs.Select(b => b.Id).ToList();
                        var c = db.Blogs.Select(b => b.Id).ToList();
                    }
                }
                """),
            BatchFixedCode = EfLintTestResources.WithModel(ModelExtension + """
                public class Sample
                {
                    public void M(BloggingContext db)
                    {
                        var a = db.Blogs.Select(b => b.Id).ToList();
                        var c = db.Blogs.Select(b => b.Id).ToList();
                    }
                }
                """),
            CodeActionValidationMode = Microsoft.CodeAnalysis.Testing.CodeActionValidationMode.None,
            NumberOfFixAllIterations = 1,
            NumberOfFixAllInDocumentIterations = 1,
            NumberOfFixAllInProjectIterations = 1,
            NumberOfIncrementalIterations = 2,
        };

        await test.RunAsync();
    }

    private static Task VerifyAsync(string before, string after) =>
        EfLintTestResources.VerifyCodeFixAsync<IncludeWithProjectionAnalyzer, IncludeWithProjectionCodeFixProvider>(
            ModelExtension + before,
            ModelExtension + after);
}
