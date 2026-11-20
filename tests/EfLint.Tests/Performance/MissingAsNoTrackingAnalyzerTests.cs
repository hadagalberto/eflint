using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Performance;

public class MissingAsNoTrackingAnalyzerTests
{
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

    [Fact]
    public async Task Count_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var c = db.Blogs.{|EFL1003:Count|}();
                }
            }
            """);
    }

    [Fact]
    public async Task SelectThenToList_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var names = db.Blogs.Select(x => x.Name).{|EFL1003:ToList|}();
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

    private static Task VerifyAsync(string code) =>
        EfLintTestResources.VerifyAnalyzerAsync<MissingAsNoTrackingAnalyzer>(code);
}
