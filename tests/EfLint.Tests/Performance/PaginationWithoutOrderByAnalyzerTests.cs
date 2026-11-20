using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Performance;

public class PaginationWithoutOrderByAnalyzerTests
{
    [Fact]
    public async Task SkipAndTake_ReportsBoth()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var page = db.Blogs.{|EFL1006:Skip|}(2).{|EFL1006:Take|}(5).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task TakeOnly_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var page = db.Blogs.{|EFL1006:Take|}(5).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task SkipAfterWhere_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var page = db.Blogs.Where(b => b.Id > 0).{|EFL1006:Skip|}(1).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task SkipAfterAsNoTracking_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var page = db.Blogs.AsNoTracking().{|EFL1006:Skip|}(1).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task TakeWithoutOrderBy_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var page = db.Blogs.{|EFL1006:Take|}(2).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task TakeAfterWhere_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var page = db.Blogs.Where(b => b.Id > 0).{|EFL1006:Take|}(1).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task SkipAndTakeAfterWhere_ReportsBoth()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var page = db.Blogs.Where(b => b.Id > 0).{|EFL1006:Skip|}(1).{|EFL1006:Take|}(2).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task OrderByThenSkipAndTake_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var page = db.Blogs.OrderBy(b => b.Id).Skip(2).Take(5).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task OrderByDescendingThenTake_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var page = db.Blogs.OrderByDescending(b => b.Id).Take(1).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task ThenByAfterOrderBy_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var page = db.Blogs.OrderBy(b => b.Name).ThenBy(b => b.Id).Skip(1).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task AsNoTrackingWithOrderBy_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var page = db.Blogs.AsNoTracking().OrderBy(b => b.Id).Skip(1).Take(2).ToList();
                }
            }
            """);
    }

    // Hand-derived: q is initialized with OrderBy(b => b.Id) before pagination, so
    // the Skip/Take chain is deterministic. EFL1006 must not fire on either call.
    [Fact]
    public async Task OrderByInLocalThenSkipAndTake_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var q = db.Blogs.OrderBy(b => b.Id);
                    var page = q.Skip(1).Take(2).ToList();
                }
            }
            """);
    }

    // Hand-derived: q aliases `ordered`, which was initialized with OrderBy. The
    // OrderBy is two local hops away from Skip, so the walk must follow both hops.
    [Fact]
    public async Task OrderByInAliasedLocalThenSkip_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var ordered = db.Blogs.OrderBy(b => b.Id);
                    IQueryable<Blog> q = ordered;
                    var page = q.Skip(1).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task InMemorySkip_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M()
                {
                    var page = new List<Blog>().Skip(1).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task SkipAfterMaterialization_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var page = db.Blogs.ToList().Skip(1).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task Count_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var count = db.Blogs.Count();
                }
            }
            """);
    }

    [Fact]
    public async Task WhereThenMaterialize_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Where(b => b.Id > 0).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task OrderByThenFirst_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blog = db.Blogs.OrderBy(b => b.Name).First();
                }
            }
            """);
    }

    private static Task VerifyAsync(string code) =>
        EfLintTestResources.VerifyAnalyzerAsync<PaginationWithoutOrderByAnalyzer>(code);
}
