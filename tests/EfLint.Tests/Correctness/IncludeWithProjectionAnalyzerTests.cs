using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Correctness;

public class IncludeWithProjectionAnalyzerTests
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
    public async Task IncludeBeforeSelect_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var ids = db.Blogs.{|EFL2006:Include|}(b => b.Posts).Select(b => b.Id).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task IncludeWithAnonymousProjection_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var rows = db.Blogs.{|EFL2006:Include|}(b => b.Posts).Select(b => new { b.Id }).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task IncludeWithStringPath_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var ids = db.Blogs.{|EFL2006:Include|}("Posts").Select(b => b.Id).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task IncludeAfterWhere_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var ids = db.Blogs.Where(b => b.Id > 0).{|EFL2006:Include|}(b => b.Posts).Select(b => b.Id).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task IncludeAfterAsNoTracking_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var ids = db.Blogs.AsNoTracking().{|EFL2006:Include|}(b => b.Posts).Select(b => b.Id).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task IncludeBeforeSelectMany_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var posts = db.Blogs.{|EFL2006:Include|}(b => b.Posts).SelectMany(b => b.Posts).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task IncludeWithoutProjection_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var blogs = db.Blogs.Include(b => b.Posts).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task ProjectionWithoutInclude_NoDiagnostic()
    {
        await VerifyAsync("""
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
    public async Task IncludeAfterSelect_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var query = db.Blogs.Select(b => b).Include("Posts");
                }
            }
            """);
    }

    [Fact]
    public async Task UserDefinedInclude_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<IncludeWithProjectionAnalyzer>("""
            public class FakeQuery<T>
            {
                public FakeQuery<T> Include(System.Linq.Expressions.Expression<System.Func<T, object>> path) => this;
                public object Select(System.Linq.Expressions.Expression<System.Func<T, object>> selector) => new object();
            }

            public class Sample
            {
                public void M(FakeQuery<Blog> query)
                {
                    var value = query.Include(b => b.Id).Select(b => b.Id);
                }
            }
            """);
    }

    [Fact]
    public async Task SeparateQueries_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var blogs = db.Blogs.Include(b => b.Posts).ToList();
                    var ids = db.Blogs.Select(b => b.Id).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task IncludeOnUnrelatedQuery_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var query = db.Blogs.Include(b => b.Posts);
                    var ids = db.Blogs.Select(b => b.Id).ToList();
                    var blogs = query.ToList();
                }
            }
            """);
    }

    private static Task VerifyAsync(string code) =>
        EfLintTestResources.VerifyAnalyzerAsync<IncludeWithProjectionAnalyzer>(ModelExtension + code);
}
