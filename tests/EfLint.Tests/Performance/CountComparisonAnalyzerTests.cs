using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Performance;

public class CountComparisonAnalyzerTests
{
    [Fact]
    public async Task CountGreaterThanZero_Reports()
    {
        await VerifyAsync("db.Blogs.{|EFL1002:Count|}() > 0");
    }

    [Fact]
    public async Task CountNotEqualsZero_Reports()
    {
        await VerifyAsync("db.Blogs.{|EFL1002:Count|}() != 0");
    }

    [Fact]
    public async Task CountGreaterThanOrEqualOne_Reports()
    {
        await VerifyAsync("db.Blogs.{|EFL1002:Count|}() >= 1");
    }

    [Fact]
    public async Task ZeroLessThanCount_Reports()
    {
        await VerifyAsync("0 < db.Blogs.{|EFL1002:Count|}()");
    }

    [Fact]
    public async Task OneLessThanOrEqualCount_Reports()
    {
        await VerifyAsync("1 <= db.Blogs.{|EFL1002:Count|}()");
    }

    [Fact]
    public async Task CountAfterWhere_Reports()
    {
        await VerifyAsync("db.Blogs.Where(b => b.Id > 0).{|EFL1002:Count|}() > 0");
    }

    [Fact]
    public async Task CountGreaterThanOne_NoDiagnostic()
    {
        await VerifyAsync("db.Blogs.Count() > 1");
    }

    [Fact]
    public async Task CountEqualsZero_NoDiagnostic()
    {
        await VerifyAsync("db.Blogs.Count() == 0");
    }

    [Fact]
    public async Task CountLessThanOne_NoDiagnostic()
    {
        await VerifyAsync("db.Blogs.Count() < 1");
    }

    [Fact]
    public async Task CountGreaterThanOrEqualTwo_NoDiagnostic()
    {
        await VerifyAsync("db.Blogs.Count() >= 2");
    }

    [Fact]
    public async Task ListCountProperty_NoDiagnostic()
    {
        await VerifyAsync("new List<Blog>().Count > 0");
    }

    [Fact]
    public async Task AnyWithoutCount_NoDiagnostic()
    {
        await VerifyAsync("db.Blogs.Any()");
    }

    [Fact]
    public async Task CountAlone_NoDiagnostic()
    {
        await VerifyAsync("db.Blogs.Count()");
    }

    private static Task VerifyAsync(string expression) =>
        EfLintTestResources.VerifyAnalyzerAsync<CountComparisonAnalyzer>($$"""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var result = {{expression}};
                }
            }
            """);
}
