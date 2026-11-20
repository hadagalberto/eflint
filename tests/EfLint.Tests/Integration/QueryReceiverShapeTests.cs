using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Integration;

/// <summary>
/// Receiver-shape coverage for every rule that decides whether a call operates on
/// an EF query. Each rule must see through a DbSet exposed directly, an
/// IQueryable&lt;T&gt; local, and a source-declared method returning IQueryable&lt;T&gt;.
/// </summary>
public class QueryReceiverShapeTests
{
    [Theory]
    [InlineData("DbSet")]
    [InlineData("IQueryableLocal")]
    [InlineData("MethodReturningIQueryable")]
    public Task EFL1001_EarlyMaterialization(string shape) =>
        EfLintTestResources.VerifyAnalyzerAsync<EarlyMaterializationAnalyzer>(Build(shape,
            "var x = {{Q}}.{|EFL1001:ToList|}().Where(b => b.Id > 0).ToList();"));

    [Theory]
    [InlineData("DbSet")]
    [InlineData("IQueryableLocal")]
    [InlineData("MethodReturningIQueryable")]
    public Task EFL1002_CountComparison(string shape) =>
        EfLintTestResources.VerifyAnalyzerAsync<CountComparisonAnalyzer>(Build(shape,
            "if ({{Q}}.{|EFL1002:Count|}() > 0) { }"));

    [Theory]
    [InlineData("DbSet")]
    [InlineData("IQueryableLocal")]
    [InlineData("MethodReturningIQueryable")]
    public Task EFL1003_MissingAsNoTracking(string shape) =>
        EfLintTestResources.VerifyAnalyzerAsync<MissingAsNoTrackingAnalyzer>(Build(shape,
            "var x = {{Q}}.{|EFL1003:ToList|}();"));

    [Theory]
    [InlineData("DbSet")]
    [InlineData("IQueryableLocal")]
    [InlineData("MethodReturningIQueryable")]
    public Task EFL1005_FirstWithoutOrderBy(string shape) =>
        EfLintTestResources.VerifyAnalyzerAsync<FirstWithoutOrderByAnalyzer>(Build(shape,
            "var x = {{Q}}.{|EFL1005:FirstOrDefault|}();"));

    [Theory]
    [InlineData("DbSet")]
    [InlineData("IQueryableLocal")]
    [InlineData("MethodReturningIQueryable")]
    public Task EFL1006_PaginationWithoutOrderBy(string shape) =>
        EfLintTestResources.VerifyAnalyzerAsync<PaginationWithoutOrderByAnalyzer>(Build(shape,
            "var x = {{Q}}.{|EFL1006:Skip|}(1).{|EFL1006:Take|}(2).ToList();"));

    [Theory]
    [InlineData("DbSet")]
    [InlineData("IQueryableLocal")]
    [InlineData("MethodReturningIQueryable")]
    public Task EFL2001_SyncOverAsync(string shape) =>
        EfLintTestResources.VerifyAnalyzerAsync<SyncOverAsyncAnalyzer>(Build(shape,
            "var x = {{Q}}.{|EFL2001:ToList|}();", isAsync: true));

    [Theory]
    [InlineData("DbSet")]
    [InlineData("IQueryableLocal")]
    [InlineData("MethodReturningIQueryable")]
    public Task EFL2003_ClientEvaluation(string shape) =>
        EfLintTestResources.VerifyAnalyzerAsync<ClientEvaluationAnalyzer>(Build(shape,
            "var x = {{Q}}.Where(b => {|EFL2003:NotTranslatable|}(b.Name)).ToList();",
            classExtra: "\n\n    private static bool NotTranslatable(string value) => value.Length > 3;"));

    [Theory]
    [InlineData("DbSet")]
    [InlineData("IQueryableLocal")]
    [InlineData("MethodReturningIQueryable")]
    public Task EFL2006_IncludeWithProjection(string shape) =>
        VerifyWithNavigationAsync(Build(shape,
            "var x = {{Q}}.{|EFL2006:Include|}(b => b.Posts).Select(b => b.Id).ToList();",
            contextType: "BloggingContext"));

    [Theory]
    [InlineData("DbSet")]
    [InlineData("IQueryableLocal")]
    [InlineData("MethodReturningIQueryable")]
    public Task EFL2008_RedundantUpdateAttach(string shape) =>
        EfLintTestResources.VerifyAnalyzerAsync<RedundantUpdateAttachAnalyzer>(Build(shape,
            "var b = {{Q}}.First();\n            db.{|EFL2008:Update|}(b);"));

    [Theory]
    [InlineData("DbSet")]
    [InlineData("IQueryableLocal")]
    [InlineData("MethodReturningIQueryable")]
    public async Task EnumerableExtensionOnDbSet_IsRecognized(string shape)
    {
        // ToList()/Count() on DbSet<T> bind to Enumerable, not Queryable. The chain
        // must still be traced back to the context for the sync-over-async rule.
        var statements = shape == "IQueryableLocal"
            ? "IQueryable<Blog> q = db.Blogs;\n            var x = q.{|EFL2001:ToList|}();"
            : shape == "MethodReturningIQueryable"
                ? "var x = Query(db).{|EFL2001:ToList|}();"
                : "var x = db.Blogs.{|EFL2001:ToList|}();";

        await EfLintTestResources.VerifyAnalyzerAsync<SyncOverAsyncAnalyzer>(
            Build(shape, statements, isAsync: true, includeLocal: false));
    }

    [Theory]
    [InlineData("DbSet")]
    [InlineData("IQueryableLocal")]
    [InlineData("MethodReturningIQueryable")]
    public Task EFL1009_LargeContains(string shape) =>
        EfLintTestResources.VerifyAnalyzerAsync<LargeContainsAnalyzer>(Build(shape,
            "var ids = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33 };\n" +
            "            var x = {{Q}}.Where(b => ids.{|EFL1009:Contains|}(b.Id)).ToList();"));

    [Fact]
    public Task NonEfQueryableLocal_NoDiagnostic() =>
        EfLintTestResources.VerifyAnalyzerAsync<SyncOverAsyncAnalyzer>("""
            public class Sample
            {
                public async Task M()
                {
                    IQueryable<int> q = new List<int>().AsQueryable();
                    var x = q.ToList();
                    await Task.CompletedTask;
                }
            }
            """);

    [Fact]
    public Task FactoryReturningNonEfQueryable_NoDiagnostic() =>
        EfLintTestResources.VerifyAnalyzerAsync<SyncOverAsyncAnalyzer>("""
            public class Sample
            {
                public async Task M()
                {
                    var x = Query().ToList();
                    await Task.CompletedTask;
                }

                private static IQueryable<int> Query() => new List<int>().AsQueryable();
            }
            """);

    [Theory]
    [InlineData("DbSet")]
    [InlineData("IQueryableLocal")]
    [InlineData("MethodReturningIQueryable")]
    public Task EFL1004_NPlusOne(string shape) =>
        VerifyWithModelAsync<NPlusOneAnalyzer>(NPlusOneModel, Build(shape,
            "foreach (var b in {{Q}}.ToList())\n            {\n                var posts = b.{|EFL1004:Posts|};\n            }",
            contextType: "NPlusOneContext"));

    [Theory]
    [InlineData("DbSet")]
    [InlineData("IQueryableLocal")]
    [InlineData("MethodReturningIQueryable")]
    public Task EFL1008_LoadToDelete(string shape) =>
        EfLintTestResources.VerifyAnalyzerAsync<LoadToDeleteUpdateAnalyzer>(Build(shape,
            "var b = {{Q}}.First();\n            db.Blogs.{|EFL1008:Remove|}(b);"));

    [Theory]
    [InlineData("DbSet")]
    [InlineData("IQueryableLocal")]
    [InlineData("MethodReturningIQueryable")]
    public Task EFL2007_StringComparison(string shape) =>
        EfLintTestResources.VerifyAnalyzerAsync<StringComparisonInQueryAnalyzer>(Build(shape,
            "var x = {{Q}}.Where(b => {|EFL2007:b.Name.ToLower() == \"x\"|}).ToList();"));

    [Theory]
    [InlineData("DbSet")]
    [InlineData("IQueryableLocal")]
    [InlineData("MethodReturningIQueryable")]
    public Task EFL1010_MultipleIncludes(string shape) =>
        VerifyWithTwoNavigationsAsync(Build(shape,
            "var x = {{Q}}.Include(b => b.Posts).{|EFL1010:Include|}(b => b.Tags).ToList();",
            contextType: "TwoNavContext"));

    private const string NavigationModel = """
        public class NavPost
        {
            public int Id { get; set; }
        }

        public class NavBlog
        {
            public int Id { get; set; }
            public List<NavPost> Posts { get; set; } = new();
        }

        public class BloggingContext : DbContext
        {
            public DbSet<NavBlog> Blogs => Set<NavBlog>();
        }

        """;

    private const string NPlusOneModel = """
        public class NpPost
        {
            public int Id { get; set; }
        }

        public class NpBlog
        {
            public int Id { get; set; }
            public List<NpPost> Posts { get; set; } = new();
        }

        public class NPlusOneContext : DbContext
        {
            public DbSet<NpBlog> Blogs => Set<NpBlog>();
            public DbSet<NpPost> Posts => Set<NpPost>();
        }

        """;

    private const string TwoNavigationModel = """
        public class NavTag
        {
            public int Id { get; set; }
        }

        public class TwoNavBlog
        {
            public int Id { get; set; }
            public List<NavPost> Posts { get; set; } = new();
            public List<NavTag> Tags { get; set; } = new();
        }

        public class TwoNavContext : DbContext
        {
            public DbSet<TwoNavBlog> Blogs => Set<TwoNavBlog>();
        }

        """;

    private static Task VerifyWithNavigationAsync(string code) =>
        VerifyWithModelAsync<IncludeWithProjectionAnalyzer>(NavigationModel, code);

    private static Task VerifyWithTwoNavigationsAsync(string code) =>
        VerifyWithModelAsync<MultipleIncludesSplitQueryAnalyzer>(NavigationModel + TwoNavigationModel, code);

    private static Task VerifyWithModelAsync<TAnalyzer>(string model, string code)
        where TAnalyzer : Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer, new()
    {
        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<TAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            ReferenceAssemblies = EfLintTestResources.EfCore,
            TestCode = EfLintTestResources.Model + model + code,
        };
        return test.RunAsync();
    }

    private static string Build(
        string shape,
        string statements,
        bool isAsync = false,
        bool includeLocal = true,
        string contextType = "BlogContext",
        string classExtra = "")
    {
        var receiver = shape switch
        {
            "DbSet" => "db.Blogs",
            "IQueryableLocal" => "q",
            "MethodReturningIQueryable" => "Query(db)",
            _ => throw new System.ArgumentOutOfRangeException(nameof(shape)),
        };

        var local = includeLocal && shape == "IQueryableLocal"
            ? $"IQueryable<{Entity(contextType)}> q = db.Blogs;\n            "
            : string.Empty;

        var helper = shape == "MethodReturningIQueryable"
            ? $"\n\n    private static IQueryable<{Entity(contextType)}> Query({contextType} db) => db.Blogs;"
            : string.Empty;

        var signature = isAsync
            ? $"public async Task M({contextType} db)"
            : $"public void M({contextType} db)";

        var awaitLine = isAsync ? "await Task.CompletedTask;\n            " : string.Empty;

        return $$"""
            public class Sample
            {
                {{signature}}
                {
                    {{awaitLine}}{{local}}{{statements.Replace("{{Q}}", receiver)}}
                }{{helper}}{{classExtra}}
            }
            """;
    }

    private static string Entity(string contextType) => contextType switch
    {
        "BloggingContext" => "NavBlog",
        "TwoNavContext" => "TwoNavBlog",
        "NPlusOneContext" => "NpBlog",
        _ => "Blog",
    };
}
