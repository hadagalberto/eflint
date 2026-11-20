using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Correctness;

public class ContextAcrossThreadsAnalyzerTests
{
    [Fact]
    public async Task ParallelForEach_CapturedContext_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ContextAcrossThreadsAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db, List<int> ids)
                {
                    Parallel.ForEach(ids, id => {|EFL2004:db|}.Blogs.Count());
                }
            }
            """);
    }

    [Fact]
    public async Task ParallelFor_CapturedContext_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ContextAcrossThreadsAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    Parallel.For(0, 10, i => {|EFL2004:db|}.Blogs.Any());
                }
            }
            """);
    }

    [Fact]
    public async Task ParallelInvoke_CapturedContext_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ContextAcrossThreadsAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    Parallel.Invoke(() => {|EFL2004:db|}.SaveChanges());
                }
            }
            """);
    }

    [Fact]
    public async Task TaskWhenAll_SelectWithContext_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ContextAcrossThreadsAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db, List<int> ids)
                {
                    var tasks = Task.WhenAll(ids.Select(id => {|EFL2004:db|}.Blogs.FirstAsync()));
                }
            }
            """);
    }

    [Fact]
    public async Task TaskWhenAll_AsyncLambdaWithContext_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ContextAcrossThreadsAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db, List<int> ids)
                {
                    var tasks = Task.WhenAll(ids.Select(async id =>
                    {
                        await {|EFL2004:db|}.SaveChangesAsync();
                    }));
                }
            }
            """);
    }

    [Fact]
    public async Task ParallelForEach_FieldContext_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ContextAcrossThreadsAnalyzer>("""
            public class Sample
            {
                private readonly BlogContext _db = new BlogContext();

                public void M(List<int> ids)
                {
                    Parallel.ForEach(ids, id => {|EFL2004:_db|}.Blogs.Count());
                }
            }
            """);
    }

    [Fact]
    public async Task ParallelForEach_NoContext_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ContextAcrossThreadsAnalyzer>("""
            public class Sample
            {
                public void M(List<int> ids)
                {
                    Parallel.ForEach(ids, id => System.Console.WriteLine(id));
                }
            }
            """);
    }

    [Fact]
    public async Task ParallelForEach_OtherCapturedType_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ContextAcrossThreadsAnalyzer>("""
            public class Sample
            {
                public void M(string prefix, List<int> ids)
                {
                    Parallel.ForEach(ids, id => System.Console.WriteLine(prefix + id));
                }
            }
            """);
    }

    [Fact]
    public async Task TaskWhenAll_NoContext_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ContextAcrossThreadsAnalyzer>("""
            public class Sample
            {
                public void M(List<int> ids)
                {
                    var tasks = Task.WhenAll(ids.Select(id => Task.Delay(id)));
                }
            }
            """);
    }

    [Fact]
    public async Task ContextUsedSequentially_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ContextAcrossThreadsAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var count = db.Blogs.Count();
                    db.SaveChanges();
                }
            }
            """);
    }

    [Fact]
    public async Task SelectWithoutWhenAll_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ContextAcrossThreadsAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db, List<int> ids)
                {
                    var queries = ids.Select(id => db.Blogs.Where(b => b.Id == id)).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task ParallelForEach_LocalContextCreatedInside_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ContextAcrossThreadsAnalyzer>("""
            public class Sample
            {
                public void M(List<int> ids)
                {
                    Parallel.ForEach(ids, id =>
                    {
                        using var local = new BlogContext();
                        local.Blogs.Count();
                    });
                }
            }
            """);
    }

    [Fact]
    public async Task ParallelForEach_BclTypeNamedContext_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<ContextAcrossThreadsAnalyzer>("""
            public class FakeContext
            {
                public void Do() { }
            }

            public class Sample
            {
                public void M(FakeContext context, List<int> ids)
                {
                    Parallel.ForEach(ids, id => context.Do());
                }
            }
            """);
    }
}
