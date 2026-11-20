using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Performance;

public class FirstWithoutOrderByAnalyzerTests
{
    [Fact]
    public async Task First_NoOrderBy_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blog = db.Blogs.{|EFL1005:First|}();
                }
            }
            """);
    }

    [Fact]
    public async Task FirstOrDefault_NoOrderBy_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blog = db.Blogs.{|EFL1005:FirstOrDefault|}();
                }
            }
            """);
    }

    [Fact]
    public async Task FirstAfterWhere_NoOrderBy_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blog = db.Blogs.Where(b => b.Id > 0).{|EFL1005:First|}();
                }
            }
            """);
    }

    [Fact]
    public async Task FirstAfterTake_NoOrderBy_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blog = db.Blogs.Take(1).{|EFL1005:First|}();
                }
            }
            """);
    }

    [Fact]
    public async Task FirstOrDefaultAfterAsNoTracking_NoOrderBy_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blog = db.Blogs.AsNoTracking().{|EFL1005:FirstOrDefault|}();
                }
            }
            """);
    }

    [Fact]
    public async Task FirstOrDefaultAfterProjection_NoOrderBy_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var name = db.Blogs.Where(b => b.Id > 0).Select(b => b.Name).{|EFL1005:FirstOrDefault|}();
                }
            }
            """);
    }

    [Fact]
    public async Task StaticQueryableFirst_NoOrderBy_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blog = Queryable.{|EFL1005:First|}(db.Blogs);
                }
            }
            """);
    }

    [Fact]
    public async Task FirstWithPredicate_NoOrderBy_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blog = db.Blogs.{|EFL1005:First|}(b => b.Id > 0);
                }
            }
            """);
    }

    [Fact]
    public async Task OrderedByFirst_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blog = db.Blogs.OrderBy(b => b.Id).First();
                }
            }
            """);
    }

    [Fact]
    public async Task OrderedByDescendingFirstOrDefault_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blog = db.Blogs.OrderByDescending(b => b.Id).FirstOrDefault();
                }
            }
            """);
    }

    [Fact]
    public async Task WhereThenOrderByFirst_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blog = db.Blogs.Where(b => b.Id > 0).OrderBy(b => b.Name).First();
                }
            }
            """);
    }

    [Fact]
    public async Task Single_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blog = db.Blogs.Single();
                }
            }
            """);
    }

    [Fact]
    public async Task FirstOnInMemoryList_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M()
                {
                    var blog = new List<Blog>().First();
                }
            }
            """);
    }

    [Fact]
    public async Task FirstAfterToList_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blog = db.Blogs.ToList().First();
                }
            }
            """);
    }

    [Fact]
    public async Task Any_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var exists = db.Blogs.Any();
                }
            }
            """);
    }

    [Fact]
    public async Task OrderingBeforeTake_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blog = db.Blogs.OrderBy(b => b.Id).Take(1).First();
                }
            }
            """);
    }

    [Fact]
    public async Task StaticQueryableFirstWithOrdering_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blog = Queryable.First(db.Blogs.OrderBy(b => b.Id));
                }
            }
            """);
    }

    [Fact]
    public async Task ThenByOrdering_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blog = db.Blogs.OrderBy(b => b.Id).ThenBy(b => b.Name).First();
                }
            }
            """);
    }

    private static Task VerifyAsync(string code) =>
        EfLintTestResources.VerifyAnalyzerAsync<FirstWithoutOrderByAnalyzer>(code);
}
