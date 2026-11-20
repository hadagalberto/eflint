using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Correctness;

public class ClientEvaluationAnalyzerTests
{
    [Fact]
    public async Task UserMethod_InWhereLambda_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ClientEvaluationAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Where(b => {|EFL2003:IsValid|}(b)).ToList();
                }

                private static bool IsValid(Blog b) => b.Id > 0;
            }
            """);
    }

    [Fact]
    public async Task UserMethod_InOrderByLambda_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ClientEvaluationAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.OrderBy(b => {|EFL2003:Normalize|}(b.Name)).ToList();
                }

                private static string Normalize(string value) => value.Trim();
            }
            """);
    }

    // Corrected per review finding EFL2003: EF Core evaluates the outermost final
    // projection on the client (Select(...).ToList() translates the query first and
    // projects afterwards), so a user method there is legal. The old assertion
    // encoded the false positive and is now a false-positive guard.
    [Fact]
    public async Task UserMethod_InFinalSelectProjection_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ClientEvaluationAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var names = db.Blogs.Select(b => Describe(b)).ToList();
                }

                private static string Describe(Blog b) => b.Name;
            }
            """);
    }

    // Hand-derived: Select is followed by Where, so its result is still composed into
    // SQL. Describe cannot be translated there -> one diagnostic on Describe.
    [Fact]
    public async Task UserMethod_InNonFinalSelect_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ClientEvaluationAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var names = db.Blogs.Select(b => {|EFL2003:Describe|}(b)).Where(name => name.Length > 0).ToList();
                }

                private static string Describe(Blog b) => b.Name;
            }
            """);
    }

    // Hand-derived: the projection is nested inside a Where lambda, so it is a
    // subquery projection rather than the outermost final one -> report Describe.
    [Fact]
    public async Task UserMethod_InSubquerySelect_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ClientEvaluationAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Where(b => db.Blogs.Select(x => {|EFL2003:Describe|}(x)).Any()).ToList();
                }

                private static string Describe(Blog b) => b.Name;
            }
            """);
    }

    [Fact]
    public async Task UserMethod_InAnyLambda_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ClientEvaluationAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var any = db.Blogs.Any(b => {|EFL2003:IsValid|}(b));
                }

                private static bool IsValid(Blog b) => b.Id > 0;
            }
            """);
    }

    [Fact]
    public async Task UserMethod_NestedInLambda_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ClientEvaluationAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Where(b => b.Id > {|EFL2003:GetThreshold|}()).ToList();
                }

                private static int GetThreshold() => 5;
            }
            """);
    }

    [Fact]
    public async Task InstanceUserMethod_InLambda_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ClientEvaluationAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Where(b => this.{|EFL2003:IsValid|}(b)).ToList();
                }

                private bool IsValid(Blog b) => b.Id > 0;
            }
            """);
    }

    [Fact]
    public async Task UserMethod_WithMultipleArguments_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ClientEvaluationAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Where(b => {|EFL2003:IsValid|}(b, 1)).ToList();
                }

                private static bool IsValid(Blog b, int threshold) => b.Id > threshold;
            }
            """);
    }

    [Fact]
    public async Task TranslatableStringMethod_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ClientEvaluationAnalyzer>("""
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
    public async Task TranslatableMathMethod_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ClientEvaluationAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Where(b => System.Math.Abs(b.Id) > 1).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task DbFunctionMarkedMethod_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ClientEvaluationAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Where(b => Normalize(b.Name) == "x").ToList();
                }

                [DbFunction("Normalize", "dbo")]
                private static string Normalize(string value) => value.Trim();
            }
            """);
    }

    [Fact]
    public async Task UserMethod_OutsideQuery_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ClientEvaluationAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var value = Normalize("abc");
                    var blogs = db.Blogs.Where(b => b.Name == value).ToList();
                }

                private static string Normalize(string value) => value.Trim();
            }
            """);
    }

    [Fact]
    public async Task UserMethod_InMemoryQuery_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ClientEvaluationAnalyzer>("""
            public class Sample
            {
                public void M()
                {
                    var items = new List<Blog>();
                    var blogs = items.Where(b => IsValid(b)).ToList();
                }

                private static bool IsValid(Blog b) => b.Id > 0;
            }
            """);
    }

    [Fact]
    public async Task EfFunctionsCall_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ClientEvaluationAnalyzer>("""
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
    public async Task ToLowerCall_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ClientEvaluationAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.Where(b => b.Name.ToLower() == "a").ToList();
                }
            }
            """);
    }
}
