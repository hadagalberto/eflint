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

    private static Task VerifyAsync(string before, string after) =>
        EfLintTestResources.VerifyCodeFixAsync<EarlyMaterializationAnalyzer, EarlyMaterializationCodeFixProvider>(
            before,
            after);
}
