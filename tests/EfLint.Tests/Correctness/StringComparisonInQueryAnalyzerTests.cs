using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Correctness;

public class StringComparisonInQueryAnalyzerTests
{
    [Fact]
    public async Task StaticEqualsWithComparison_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<StringComparisonInQueryAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Where(b => {|EFL2007:string.Equals(b.Name, "a", StringComparison.OrdinalIgnoreCase)|}).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task InstanceEqualsWithComparison_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<StringComparisonInQueryAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db, string search)
                {
                    var blogs = db.Blogs.Where(b => {|EFL2007:b.Name.Equals(search, StringComparison.OrdinalIgnoreCase)|}).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task ToLowerEquality_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<StringComparisonInQueryAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Where(b => {|EFL2007:b.Name.ToLower() == "a"|}).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task ToUpperInequality_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<StringComparisonInQueryAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Where(b => {|EFL2007:b.Name.ToUpper() != "a"|}).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task ToLowerInvariantEquality_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<StringComparisonInQueryAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Where(b => {|EFL2007:b.Name.ToLowerInvariant() == "a"|}).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task StringComparisonCurrentCulture_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<StringComparisonInQueryAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db, string search)
                {
                    var blogs = db.Blogs.Where(b => {|EFL2007:string.Equals(b.Name, search, StringComparison.CurrentCulture)|}).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task InAnyLambda_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<StringComparisonInQueryAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var any = db.Blogs.Any(b => {|EFL2007:b.Name.ToLower() == "a"|});
                }
            }
            """);
    }

    [Fact]
    public async Task PlainEquality_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<StringComparisonInQueryAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Where(b => b.Name == "a").ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task EqualsWithoutComparison_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<StringComparisonInQueryAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Where(b => string.Equals(b.Name, "a")).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task StartsWith_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<StringComparisonInQueryAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Where(b => b.Name.StartsWith("a")).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task EfFunctionsLike_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<StringComparisonInQueryAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Where(b => EF.Functions.Like(b.Name, "a%")).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task StringEqualsOutsideQuery_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<StringComparisonInQueryAnalyzer>("""
            public class Sample
            {
                public void M(string search)
                {
                    var match = string.Equals(search, "a", StringComparison.OrdinalIgnoreCase);
                }
            }
            """);
    }

    [Fact]
    public async Task ToLowerComparisonOnInMemoryList_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<StringComparisonInQueryAnalyzer>("""
            public class Sample
            {
                public void M()
                {
                    var items = new List<Blog>();
                    var blogs = items.Where(b => b.Name.ToLower() == "a").ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task StringCompare_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<StringComparisonInQueryAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Where(b => string.Compare(b.Name, "a", StringComparison.Ordinal) == 0).ToList();
                }
            }
            """);
    }
}
