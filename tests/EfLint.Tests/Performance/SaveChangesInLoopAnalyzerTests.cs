using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Performance;

public class SaveChangesInLoopAnalyzerTests
{
    [Fact]
    public async Task ForeachWithSaveChanges_Reports()
    {
        await VerifyAsync("""
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
            """);
    }

    [Fact]
    public async Task ForWithSaveChanges_Reports()
    {
        await VerifyAsync("""
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
            """);
    }

    [Fact]
    public async Task WhileWithSaveChanges_Reports()
    {
        await VerifyAsync("""
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
            """);
    }

    [Fact]
    public async Task DoWhileWithSaveChanges_Reports()
    {
        await VerifyAsync("""
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
            """);
    }

    [Fact]
    public async Task ForeachWithPropertyAssignment_Reports()
    {
        await VerifyAsync("""
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
            """);
    }

    [Fact]
    public async Task ForeachWithDbSetAdd_Reports()
    {
        await VerifyAsync("""
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
            """);
    }

    [Fact]
    public async Task ForeachWithSaveChangesAsync_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public async Task M(BlogContext db, List<Blog> blogs)
                {
                    foreach (var b in blogs)
                    {
                        await db.{|EFL1007:SaveChangesAsync|}();
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task SaveChangesOutsideLoop_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    db.SaveChanges();
                }
            }
            """);
    }

    [Fact]
    public async Task LoopWithoutSaveChanges_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(List<Blog> blogs)
                {
                    foreach (var b in blogs)
                    {
                        var name = b.Name;
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task UserClassSaveChangesInLoop_NoDiagnostic()
    {
        await VerifyAsync("""
            public class FakeContext
            {
                public void SaveChanges() { }
            }

            public class Sample
            {
                public void M(FakeContext db, List<Blog> blogs)
                {
                    foreach (var b in blogs)
                    {
                        db.SaveChanges();
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task SaveChangesInLambdaOutsideLoop_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    Action save = () => db.SaveChanges();
                }
            }
            """);
    }

    [Fact]
    public async Task SaveChangesAsyncOutsideLoop_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public async Task M(BlogContext db)
                {
                    await db.SaveChangesAsync();
                }
            }
            """);
    }

    [Fact]
    public async Task ForeachWithOnlyAdd_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db, List<Blog> blogs)
                {
                    foreach (var b in blogs)
                    {
                        db.Blogs.Add(b);
                    }
                }
            }
            """);
    }

    private static Task VerifyAsync(string code) =>
        EfLintTestResources.VerifyAnalyzerAsync<SaveChangesInLoopAnalyzer>(code);
}
