using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.CodeFixes.Performance;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Performance;

public class EarlyMaterializationCodeFixTests
{
    [Fact]
    public async Task ToListBeforeWhereAndSelect_MovedToEnd()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var names = db.Blogs.{|EFL1001:ToList|}().Where(b => b.Id > 0).Select(b => b.Name);
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var names = db.Blogs.Where(b => b.Id > 0).Select(b => b.Name).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task ToArrayBeforeWhere_MovedToEnd()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.{|EFL1001:ToArray|}().Where(b => b.Id > 0);
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Where(b => b.Id > 0).ToArray();
                }
            }
            """);
    }

    [Fact]
    public async Task AsEnumerableBeforeWhere_MovedToEnd()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.{|EFL1001:AsEnumerable|}().Where(b => b.Id > 0);
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Where(b => b.Id > 0).AsEnumerable();
                }
            }
            """);
    }

    [Fact]
    public async Task ToListBeforeOrderBy_MovedToEnd()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.{|EFL1001:ToList|}().OrderBy(b => b.Id);
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.OrderBy(b => b.Id).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task ToListBeforeSkipAndTake_MovedToEnd()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.{|EFL1001:ToList|}().Skip(1).Take(2);
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Skip(1).Take(2).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task ToListAfterWhereBeforeSelect_MovedToEnd()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var names = db.Blogs.Where(b => b.Id > 0).{|EFL1001:ToList|}().Select(b => b.Name);
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var names = db.Blogs.Where(b => b.Id > 0).Select(b => b.Name).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task FixAll_MultipleMaterializers_MovedToEnd()
    {
        await EfLintTestResources.VerifyFixAllAsync<EarlyMaterializationAnalyzer, EarlyMaterializationCodeFixProvider>(
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var names = db.Blogs.{|EFL1001:ToList|}().Where(b => b.Id > 0).Select(b => b.Name);
                    var blogs = db.Blogs.{|EFL1001:ToArray|}().OrderBy(b => b.Id);
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var names = db.Blogs.Where(b => b.Id > 0).Select(b => b.Name).ToList();
                    var blogs = db.Blogs.OrderBy(b => b.Id).ToArray();
                }
            }
            """);
    }

    // FixedCode is compiled by the test harness: db.Blogs.Count() is valid EF Core 8.
    [Fact]
    public async Task AsEnumerableBeforeCount_MaterializerRemoved()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var count = db.Blogs.{|EFL1001:AsEnumerable|}().Count();
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var count = db.Blogs.Count();
                }
            }
            """);
    }

    // Hand-derived: moving ToList to the end must not leave the original terminal ToList
    // in place, otherwise the result is .Where(...).ToList().ToList().
    [Fact]
    public async Task ToListBeforeWhereThenToList_NoDuplicateMaterializer()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.{|EFL1001:ToList|}().Where(b => b.Id > 0).ToList();
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Where(b => b.Id > 0).ToList();
                }
            }
            """);
    }

    // Hand-derived: only the translatable Where may move to the server. The client-only
    // predicate must keep running on List<Blog>, so ToList is inserted before it.
    [Fact]
    public async Task ClientOnlyPredicate_MaterializationStaysBeforeIt()
    {
        await VerifyAsync(
            """
            public static class Helpers
            {
                public static bool IsCool(Blog b) => b.Id > 0;
            }

            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.{|EFL1001:ToList|}().Where(b => b.Id > 0).Where(b => Helpers.IsCool(b));
                }
            }
            """,
            """
            public static class Helpers
            {
                public static bool IsCool(Blog b) => b.Id > 0;
            }

            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Where(b => b.Id > 0).ToList().Where(b => Helpers.IsCool(b));
                }
            }
            """);
    }

    private static Task VerifyAsync(string before, string after) =>
        EfLintTestResources.VerifyCodeFixAsync<EarlyMaterializationAnalyzer, EarlyMaterializationCodeFixProvider>(
            before,
            after);
}
