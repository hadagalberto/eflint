using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Performance;

public class MissingAsNoTrackingAnalyzerTests
{
    // Extra model with a navigation; names differ from the shared harness model.
    private const string RelationalModel = """
        public class PostEntity
        {
            public int Id { get; set; }
            public int BlogId { get; set; }
        }

        public class BlogWithPosts
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public List<PostEntity> Posts { get; set; } = new();
        }

        public class RelationalContext : DbContext
        {
            public DbSet<BlogWithPosts> Blogs => Set<BlogWithPosts>();
            public DbSet<PostEntity> Posts => Set<PostEntity>();
        }

        """;

    [Fact]
    public async Task ToList_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var list = db.Blogs.{|EFL1003:ToList|}();
                }
            }
            """);
    }

    [Fact]
    public async Task First_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var b = db.Blogs.{|EFL1003:First|}();
                }
            }
            """);
    }

    // Corrected: Count() materializes a scalar, never an entity. AsNoTracking() cannot be
    // applied to the resulting int, so this case must stay silent (review defect 1).
    [Fact]
    public async Task Count_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var c = db.Blogs.Count();
                }
            }
            """);
    }

    // Corrected: projecting to string produces IQueryable<string>, not an entity query.
    // Adding AsNoTracking() there produces CS0452 (review defect 1).
    [Fact]
    public async Task SelectThenToList_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var names = db.Blogs.Select(x => x.Name).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task WhereThenFirstOrDefault_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var b = db.Blogs.Where(x => x.Id > 0).{|EFL1003:FirstOrDefault|}();
                }
            }
            """);
    }

    [Fact]
    public async Task ToArray_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var arr = db.Blogs.{|EFL1003:ToArray|}();
                }
            }
            """);
    }

    [Fact]
    public async Task AsNoTracking_NoDiagnostic()
    {
        await VerifyAsync("""
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
    public async Task AsTracking_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var list = db.Blogs.AsTracking().ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task SaveChangesAfterQuery_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var list = db.Blogs.ToList();
                    db.SaveChanges();
                }
            }
            """);
    }

    [Fact]
    public async Task AddAfterQuery_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var list = db.Blogs.ToList();
                    db.Blogs.Add(new Blog());
                }
            }
            """);
    }

    [Fact]
    public async Task EntityPropertyAssignedAfterQuery_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var b = db.Blogs.First();
                    b.Name = "x";
                }
            }
            """);
    }

    [Fact]
    public async Task LinqToObjectsQuery_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M()
                {
                    var list = new List<Blog>().ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task QueryableReturnedWithoutMaterializing_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public IQueryable<Blog> M(BlogContext db)
                {
                    return db.Blogs.Where(x => x.Id > 0);
                }
            }
            """);
    }

    // Entity-valued identity projection: materialized element is the mapped entity Blog.
    [Fact]
    public async Task EntityIdentitySelectThenToList_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Select(x => x).{|EFL1003:ToList|}();
                }
            }
            """);
    }

    // Navigation projection still produces a mapped entity (Post), so the rule applies.
    [Fact]
    public async Task NavigationSelectManyThenToList_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<MissingAsNoTrackingAnalyzer>(RelationalModel + """
            public class Sample
            {
                public void M(RelationalContext db)
                {
                    var posts = db.Blogs.SelectMany(b => b.Posts).{|EFL1003:ToList|}();
                }
            }
            """);
    }

    // Hand-derived: b.Posts.Count() projects an int, so ToList() materializes IQueryable<int>.
    [Fact]
    public async Task ScalarCountSubqueryProjection_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<MissingAsNoTrackingAnalyzer>(RelationalModel + """
            public class Sample
            {
                public void M(RelationalContext db)
                {
                    var counts = db.Blogs.Select(b => b.Posts.Count()).ToList();
                }
            }
            """);
    }

    // Hand-derived: an anonymous type is not a mapped entity.
    [Fact]
    public async Task AnonymousTypeProjection_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var rows = db.Blogs.Select(b => new { b.Id, b.Name }).ToList();
                }
            }
            """);
    }

    // Hand-derived: Any() returns bool; there is no entity to stop tracking.
    [Fact]
    public async Task Any_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var any = db.Blogs.Any();
                }
            }
            """);
    }

    // Hand-derived: a materializer inside an expression lambda is a subquery, never a
    // standalone read-only query whose result the enclosing method can inspect.
    [Fact]
    public async Task MaterializerInsideExpressionLambda_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M()
                {
                    System.Func<BlogContext, List<Blog>> load = db => db.Blogs.ToList();
                }
            }
            """);
    }

    private static Task VerifyAsync(string code) =>
        EfLintTestResources.VerifyAnalyzerAsync<MissingAsNoTrackingAnalyzer>(code);
}
