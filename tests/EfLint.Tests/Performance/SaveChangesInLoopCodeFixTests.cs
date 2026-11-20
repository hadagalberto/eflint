using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.CodeFixes.Performance;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Performance;

public class SaveChangesInLoopCodeFixTests
{
    [Fact]
    public async Task Foreach_SaveMovedAfterLoop()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BlogContext db, List<Blog> blogs)
                {
                    foreach (var b in blogs)
                    {
                        db.{|EFL1007:SaveChanges|}();
                    }
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db, List<Blog> blogs)
                {
                    foreach (var b in blogs)
                    {
                    }
                    db.SaveChanges();
                }
            }
            """);
    }

    [Fact]
    public async Task For_SaveMovedAfterLoop()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        db.{|EFL1007:SaveChanges|}();
                    }
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    for (int i = 0; i < 10; i++)
                    {
                    }
                    db.SaveChanges();
                }
            }
            """);
    }

    [Fact]
    public async Task While_SaveMovedAfterLoop()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    int i = 0;
                    while (i < 10)
                    {
                        i++;
                        db.{|EFL1007:SaveChanges|}();
                    }
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    int i = 0;
                    while (i < 10)
                    {
                        i++;
                    }
                    db.SaveChanges();
                }
            }
            """);
    }

    [Fact]
    public async Task DoWhile_SaveMovedAfterLoop()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    int i = 0;
                    do
                    {
                        db.{|EFL1007:SaveChanges|}();
                        i++;
                    }
                    while (i < 10);
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db)
                {
                    int i = 0;
                    do
                    {
                        i++;
                    }
                    while (i < 10);
                    db.SaveChanges();
                }
            }
            """);
    }

    [Fact]
    public async Task ForeachWithPropertyAssignment_SaveMovedAfterLoop()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BlogContext db, List<Blog> blogs)
                {
                    foreach (var b in blogs)
                    {
                        b.Name = "x";
                        db.{|EFL1007:SaveChanges|}();
                    }
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db, List<Blog> blogs)
                {
                    foreach (var b in blogs)
                    {
                        b.Name = "x";
                    }
                    db.SaveChanges();
                }
            }
            """);
    }

    [Fact]
    public async Task ForeachWithDbSetAdd_SaveMovedAfterLoop()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BlogContext db, List<Blog> blogs)
                {
                    foreach (var b in blogs)
                    {
                        db.Blogs.Add(b);
                        db.{|EFL1007:SaveChanges|}();
                    }
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db, List<Blog> blogs)
                {
                    foreach (var b in blogs)
                    {
                        db.Blogs.Add(b);
                    }
                    db.SaveChanges();
                }
            }
            """);
    }

    [Fact]
    public async Task FixAll_TwoLoops_SavesMovedAfterEachLoop()
    {
        await EfLintTestResources.VerifyFixAllAsync<SaveChangesInLoopAnalyzer, SaveChangesInLoopCodeFixProvider>(
            """
            public class Sample
            {
                public void M(BlogContext db, List<Blog> blogs)
                {
                    foreach (var b in blogs)
                    {
                        db.Blogs.Add(b);
                        db.{|EFL1007:SaveChanges|}();
                    }
                    foreach (var b in blogs)
                    {
                        db.Blogs.Add(b);
                        db.{|EFL1007:SaveChanges|}();
                    }
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db, List<Blog> blogs)
                {
                    foreach (var b in blogs)
                    {
                        db.Blogs.Add(b);
                    }
                    db.SaveChanges();
                    foreach (var b in blogs)
                    {
                        db.Blogs.Add(b);
                    }
                    db.SaveChanges();
                }
            }
            """);
    }

    private static Task VerifyAsync(string before, string after) =>
        EfLintTestResources.VerifyCodeFixAsync<SaveChangesInLoopAnalyzer, SaveChangesInLoopCodeFixProvider>(
            before,
            after);
}
