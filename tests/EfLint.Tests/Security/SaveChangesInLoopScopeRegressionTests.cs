using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.CodeFixes.Performance;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Security;

/// <summary>
/// Regression coverage for EFL1007. The analyzer still reports the diagnostic;
/// the defect is that the code fix moved a SaveChanges call whose receiver is a
/// local declared inside the loop, producing CS0103 after the loop.
/// </summary>
public class SaveChangesInLoopScopeRegressionTests
{
    private const string LoopLocalContextCode = """
        public class Sample
        {
            public void M(System.Collections.Generic.List<int> ids)
            {
                foreach (var id in ids)
                {
                    var ctx = new BlogContext();
                    ctx.Blogs.Add(new Blog { Id = id });
                    ctx.SaveChanges();
                }
            }
        }
        """;

    [Fact]
    public async Task Analyzer_StillReportsEfl1007_WhenContextIsLoopLocal()
    {
        // The diagnostic is correct; only the fix must be suppressed.
        await EfLintTestResources.VerifyAnalyzerAsync<SaveChangesInLoopAnalyzer>(
            """
            public class Sample
            {
                public void M(System.Collections.Generic.List<int> ids)
                {
                    foreach (var id in ids)
                    {
                        var ctx = new BlogContext();
                        ctx.Blogs.Add(new Blog { Id = id });
                        ctx.{|EFL1007:SaveChanges|}();
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task CodeFix_NotOffered_WhenContextIsLoopLocal()
    {
        // Hand-derived: 'ctx' is declared inside the foreach body and is out of
        // scope immediately after it, so moving the call cannot compile.
        Assert.Equal(0, await CodeFixOfferProbe.CountAsync<SaveChangesInLoopAnalyzer, SaveChangesInLoopCodeFixProvider>(
            LoopLocalContextCode,
            EfLintDiagnostics.SaveChangesInLoopId));
    }

    [Fact]
    public async Task CodeFix_FieldBackedContext_StillMovedAndCompiles()
    {
        // Hand-derived: '_db' is a field and stays in scope after the loop.
        await EfLintTestResources.VerifyCodeFixAsync<SaveChangesInLoopAnalyzer, SaveChangesInLoopCodeFixProvider>(
            """
            public class Sample
            {
                private readonly BlogContext _db = new BlogContext();

                public void M(System.Collections.Generic.List<int> ids)
                {
                    foreach (var id in ids)
                    {
                        _db.Blogs.Add(new Blog { Id = id });
                        _db.{|EFL1007:SaveChanges|}();
                    }
                }
            }
            """,
            """
            public class Sample
            {
                private readonly BlogContext _db = new BlogContext();

                public void M(System.Collections.Generic.List<int> ids)
                {
                    foreach (var id in ids)
                    {
                        _db.Blogs.Add(new Blog { Id = id });
                    }
                    _db.SaveChanges();
                }
            }
            """);
    }
}
