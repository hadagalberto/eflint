using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Performance;

public class LargeContainsAnalyzerTests
{
    [Fact]
    public async Task LocalArray_InWhereLambda_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LargeContainsAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var ids = new[] {1, 2, 3, 4, 5};
                    var blogs = db.Blogs.Where(b => ids.{|EFL1009:Contains|}(b.Id)).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task LocalList_InWhereLambda_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LargeContainsAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var ids = new List<int> {1, 2, 3, 4, 5};
                    var blogs = db.Blogs.Where(b => ids.{|EFL1009:Contains|}(b.Id)).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task InlineArray_InWhereLambda_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LargeContainsAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Where(b => new[] {1, 2, 3, 4}.{|EFL1009:Contains|}(b.Id)).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task LocalHashSet_InCountLambda_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LargeContainsAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var ids = new HashSet<int> {1, 2, 3, 4};
                    var count = db.Blogs.Count(b => ids.{|EFL1009:Contains|}(b.Id));
                }
            }
            """);
    }

    [Fact]
    public async Task LocalList_InAnyLambda_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LargeContainsAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var ids = new List<int> {1, 2, 3, 4};
                    var any = db.Blogs.Any(b => ids.{|EFL1009:Contains|}(b.Id));
                }
            }
            """);
    }

    [Fact]
    public async Task LocalArray_InWhereAfterOrderBy_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LargeContainsAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var ids = new[] {1, 2, 3, 4};
                    var blogs = db.Blogs.OrderBy(b => b.Id).Where(b => ids.{|EFL1009:Contains|}(b.Id)).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task InlineArray_ExactlyAtThreshold_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LargeContainsAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Where(b => new[] {1, 2, 3}.{|EFL1009:Contains|}(b.Id)).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task SmallArray_InWhereLambda_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LargeContainsAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var ids = new[] {1, 2};
                    var blogs = db.Blogs.Where(b => ids.Contains(b.Id)).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task NoContains_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LargeContainsAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Where(b => b.Id == 1).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task LargeList_OutsideQuery_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LargeContainsAnalyzer>("""
            public class Sample
            {
                public void M()
                {
                    var ids = new List<int> {1, 2, 3, 4};
                    ids.Contains(1);
                }
            }
            """);
    }

    [Fact]
    public async Task StringContains_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LargeContainsAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Where(b => "abc".Contains(b.Name)).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task LargeArray_InMemoryQuery_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LargeContainsAnalyzer>("""
            public class Sample
            {
                public void M()
                {
                    var ids = new[] {1, 2, 3, 4};
                    var blogs = new List<Blog>().Where(x => ids.Contains(x.Id)).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task StringPropertyContains_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LargeContainsAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Where(b => b.Name.Contains("a")).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task ParameterCollection_CountUnknown_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LargeContainsAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db, int[] ids)
                {
                    var blogs = db.Blogs.Where(b => ids.Contains(b.Id)).ToList();
                }
            }
            """);
    }
}
