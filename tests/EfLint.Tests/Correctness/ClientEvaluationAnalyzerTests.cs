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

    [Fact]
    public async Task UserMethod_InSelectLambda_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ClientEvaluationAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var names = db.Blogs.Select(b => {|EFL2003:Describe|}(b)).ToList();
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
