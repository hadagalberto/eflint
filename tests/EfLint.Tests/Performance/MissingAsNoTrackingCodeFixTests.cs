using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.CodeFixes.Performance;
using EfLint.Tests.TestInfrastructure;
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

    [Fact]
    public async Task Count_AsNoTrackingInserted()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var c = db.Blogs.{|EFL1003:Count|}();
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var c = db.Blogs.AsNoTracking().Count();
                }
            }
            """);
    }

    [Fact]
    public async Task SelectThenToList_AsNoTrackingInserted()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var names = db.Blogs.Select(x => x.Name).{|EFL1003:ToList|}();
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var names = db.Blogs.Select(x => x.Name).AsNoTracking().ToList();
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

    private static Task VerifyAsync(string before, string after) =>
        EfLintTestResources.VerifyCodeFixAsync<MissingAsNoTrackingAnalyzer, MissingAsNoTrackingCodeFixProvider>(
            before,
            after);
}
