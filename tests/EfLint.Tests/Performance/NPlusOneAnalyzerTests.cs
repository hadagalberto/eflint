using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Performance;

public class NPlusOneAnalyzerTests
{
    private const string Model = """
        public class Post
        {
            public int Id { get; set; }
            public int BlogId { get; set; }
        }

        public class BlogWithPosts
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public List<Post> Posts { get; set; } = new();
        }

        public class BloggingContext : DbContext
        {
            public DbSet<BlogWithPosts> Blogs => Set<BlogWithPosts>();
            public DbSet<Post> Posts => Set<Post>();
        }

        """;

    [Fact]
    public async Task NavigationAccessInLoop_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    foreach (var b in db.Blogs.ToList())
                    {
                        var posts = b.{|EFL1004:Posts|};
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task PerItemCountQueryInLoop_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    foreach (var b in db.Blogs.ToList())
                    {
                        var c = db.Posts.{|EFL1004:Count|}(p => p.BlogId == b.Id);
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task NavigationUsedAsInnerLoopCollection_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    foreach (var b in db.Blogs.ToList())
                    {
                        foreach (var p in b.{|EFL1004:Posts|})
                        {
                        }
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task NavigationAccessAfterFilteredQuery_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    foreach (var b in db.Blogs.Where(x => x.Id > 0).ToList())
                    {
                        var p = b.{|EFL1004:Posts|};
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task NavigationAccessWithLinqOperator_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    foreach (var b in db.Blogs.ToList())
                    {
                        var first = b.{|EFL1004:Posts|}.FirstOrDefault();
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task PerItemWhereQueryInLoop_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    foreach (var b in db.Blogs.ToList())
                    {
                        var c = db.Posts.{|EFL1004:Where|}(p => p.BlogId == b.Id).Count();
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task IncludedNavigationLambda_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    foreach (var b in db.Blogs.Include(x => x.Posts).ToList())
                    {
                        var posts = b.Posts;
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task InMemoryCollection_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M()
                {
                    foreach (var b in new List<BlogWithPosts>())
                    {
                        var posts = b.Posts;
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task ScalarPropertyAccess_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    foreach (var b in db.Blogs.ToList())
                    {
                        var id = b.Id;
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task EmptyLoop_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    foreach (var b in db.Blogs.ToList())
                    {
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task SelectProjectionOutsideLoop_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var all = db.Blogs.ToList().Select(b => b.Posts).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task ScalarNavigationIdAccess_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    foreach (var p in db.Posts.ToList())
                    {
                        var id = p.BlogId;
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task IncludedNavigationStringPath_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    foreach (var b in db.Blogs.Include("Posts").ToList())
                    {
                        var posts = b.Posts;
                    }
                }
            }
            """);
    }

    private static Task VerifyAsync(string code) =>
        EfLintTestResources.VerifyAnalyzerAsync<NPlusOneAnalyzer>(Model + code);
}
