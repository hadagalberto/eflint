using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.CodeFixes.Performance;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Performance;

public class CountComparisonCodeFixTests
{
    [Fact]
    public async Task CountGreaterThanZero_UsesAny()
    {
        await VerifyAsync("db.Blogs.{|EFL1002:Count|}() > 0", "db.Blogs.Any()");
    }

    [Fact]
    public async Task CountNotEqualsZero_UsesAny()
    {
        await VerifyAsync("db.Blogs.{|EFL1002:Count|}() != 0", "db.Blogs.Any()");
    }

    [Fact]
    public async Task CountGreaterThanOrEqualOne_UsesAny()
    {
        await VerifyAsync("db.Blogs.{|EFL1002:Count|}() >= 1", "db.Blogs.Any()");
    }

    [Fact]
    public async Task ZeroLessThanCount_UsesAny()
    {
        await VerifyAsync("0 < db.Blogs.{|EFL1002:Count|}()", "db.Blogs.Any()");
    }

    [Fact]
    public async Task OneLessThanOrEqualCount_UsesAny()
    {
        await VerifyAsync("1 <= db.Blogs.{|EFL1002:Count|}()", "db.Blogs.Any()");
    }

    [Fact]
    public async Task CountAfterWhere_UsesAny()
    {
        await VerifyAsync(
            "db.Blogs.Where(b => b.Id > 0).{|EFL1002:Count|}() > 0",
            "db.Blogs.Where(b => b.Id > 0).Any()");
    }

    // Hand-derived: keep the await, swap CountAsync for AnyAsync; the fixed code
    // must compile against real EF Core 8 references.
    [Fact]
    public async Task AwaitCountAsyncGreaterThanZero_UsesAnyAsync()
    {
        await EfLintTestResources.VerifyCodeFixAsync<CountComparisonAnalyzer, CountComparisonCodeFixProvider>(
            """
            public class Sample
            {
                public async Task<bool> M(BlogContext db)
                {
                    return await db.Blogs.{|EFL1002:CountAsync|}() > 0;
                }
            }
            """,
            """
            public class Sample
            {
                public async Task<bool> M(BlogContext db)
                {
                    return await db.Blogs.AnyAsync();
                }
            }
            """);
    }

    // Hand-derived: the predicate must survive the CountAsync -> AnyAsync rename.
    [Fact]
    public async Task AwaitCountAsyncWithPredicate_UsesAnyAsync()
    {
        await EfLintTestResources.VerifyCodeFixAsync<CountComparisonAnalyzer, CountComparisonCodeFixProvider>(
            """
            public class Sample
            {
                public async Task<bool> M(BlogContext db)
                {
                    return await db.Blogs.{|EFL1002:CountAsync|}(b => b.Id > 0) > 0;
                }
            }
            """,
            """
            public class Sample
            {
                public async Task<bool> M(BlogContext db)
                {
                    return await db.Blogs.AnyAsync(b => b.Id > 0);
                }
            }
            """);
    }

    [Fact]
    public async Task FixAll_MultipleComparisons_UsesAny()
    {
        await EfLintTestResources.VerifyFixAllAsync<CountComparisonAnalyzer, CountComparisonCodeFixProvider>(
            WrapTwo("db.Blogs.{|EFL1002:Count|}() > 0", "db.Blogs.{|EFL1002:Count|}() != 0"),
            WrapTwo("db.Blogs.Any()", "db.Blogs.Any()"));
    }

    private static Task VerifyAsync(string beforeExpression, string afterExpression) =>
        EfLintTestResources.VerifyCodeFixAsync<CountComparisonAnalyzer, CountComparisonCodeFixProvider>(
            Wrap(beforeExpression),
            Wrap(afterExpression));

    private static string Wrap(string expression) => $$"""
        public class Sample
        {
            public void M(BlogContext db)
            {
                var result = {{expression}};
            }
        }
        """;

    private static string WrapTwo(string first, string second) => $$"""
        public class Sample
        {
            public void M(BlogContext db)
            {
                var result = {{first}};
                var other = {{second}};
            }
        }
        """;
}
