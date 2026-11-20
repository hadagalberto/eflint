using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Performance;

public class EarlyMaterializationAnalyzerTests
{
    [Fact]
    public async Task ToListBeforeWhereAndSelect_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var names = db.Blogs.{|EFL1001:ToList|}().Where(b => b.Id > 0).Select(b => b.Name);
                }
            }
            """);
    }

    [Fact]
    public async Task ToArrayBeforeWhere_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.{|EFL1001:ToArray|}().Where(b => b.Id > 0);
                }
            }
            """);
    }

    [Fact]
    public async Task AsEnumerableBeforeWhere_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.{|EFL1001:AsEnumerable|}().Where(b => b.Id > 0);
                }
            }
            """);
    }

    [Fact]
    public async Task ToListBeforeOrderBy_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.{|EFL1001:ToList|}().OrderBy(b => b.Id);
                }
            }
            """);
    }

    [Fact]
    public async Task ToListBeforeSkipAndTake_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.{|EFL1001:ToList|}().Skip(1).Take(2);
                }
            }
            """);
    }

    [Fact]
    public async Task ToListAfterWhereBeforeSelect_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var names = db.Blogs.Where(b => b.Id > 0).{|EFL1001:ToList|}().Select(b => b.Name);
                }
            }
            """);
    }

    [Fact]
    public async Task ToListAtEndOfQuery_NoDiagnostic()
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
    public async Task ToListAlone_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task MaterializationOfInMemoryList_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M()
                {
                    var blogs = new List<Blog>().ToList().Where(b => b.Id > 0);
                }
            }
            """);
    }

    [Fact]
    public async Task MaterializerFollowedByNonQueryMethod_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var text = db.Blogs.ToList().ToString();
                }
            }
            """);
    }

    [Fact]
    public async Task UserDefinedToListOnNonEfType_NoDiagnostic()
    {
        await VerifyAsync("""
            public class FakeQuery
            {
                public List<Blog> ToList() => new List<Blog>();
            }

            public class Sample
            {
                public void M()
                {
                    var blogs = new FakeQuery().ToList().Where(b => b.Id > 0);
                }
            }
            """);
    }

    [Fact]
    public async Task ProjectionThenMaterialize_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var names = db.Blogs.Select(b => b.Name).ToList();
                }
            }
            """);
    }

    private static Task VerifyAsync(string code) =>
        EfLintTestResources.VerifyAnalyzerAsync<EarlyMaterializationAnalyzer>(code);
}
