using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Correctness;

public class SyncOverAsyncAnalyzerTests
{
    [Fact]
    public async Task ToList_InAsyncMethod_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SyncOverAsyncAnalyzer>("""
            public class Sample
            {
                public async Task M(BlogContext db)
                {
                    var blogs = db.Blogs.{|EFL2001:ToList|}();
                }
            }
            """);
    }

    [Fact]
    public async Task Count_InAsyncMethod_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SyncOverAsyncAnalyzer>("""
            public class Sample
            {
                public async Task<int> M(BlogContext db)
                {
                    return db.Blogs.{|EFL2001:Count|}();
                }
            }
            """);
    }

    [Fact]
    public async Task First_InAsyncMethod_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SyncOverAsyncAnalyzer>("""
            public class Sample
            {
                public async Task<Blog> M(BlogContext db)
                {
                    return db.Blogs.{|EFL2001:First|}();
                }
            }
            """);
    }

    [Fact]
    public async Task Any_AfterWhere_InAsyncMethod_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SyncOverAsyncAnalyzer>("""
            public class Sample
            {
                public async Task<bool> M(BlogContext db)
                {
                    return db.Blogs.Where(b => b.Id > 0).{|EFL2001:Any|}();
                }
            }
            """);
    }

    [Fact]
    public async Task SaveChanges_InAsyncMethod_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SyncOverAsyncAnalyzer>("""
            public class Sample
            {
                public async Task M(BlogContext db)
                {
                    db.Blogs.Add(new Blog());
                    db.{|EFL2001:SaveChanges|}();
                }
            }
            """);
    }

    [Fact]
    public async Task Find_InAsyncMethod_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SyncOverAsyncAnalyzer>("""
            public class Sample
            {
                public async Task<Blog> M(BlogContext db)
                {
                    return db.{|EFL2001:Find<Blog>|}(1);
                }
            }
            """);
    }

    [Fact]
    public async Task ToArray_InAsyncMethod_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SyncOverAsyncAnalyzer>("""
            public class Sample
            {
                public async Task M(BlogContext db)
                {
                    var blogs = db.Blogs.{|EFL2001:ToArray|}();
                }
            }
            """);
    }

    [Fact]
    public async Task FirstOrDefault_AfterOrderBy_InAsyncMethod_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SyncOverAsyncAnalyzer>("""
            public class Sample
            {
                public async Task<Blog> M(BlogContext db)
                {
                    return db.Blogs.OrderBy(b => b.Id).{|EFL2001:FirstOrDefault|}();
                }
            }
            """);
    }

    // Hand-derived: DbSet<T>.Find is the synchronous lookup with an async equivalent,
    // just like DbContext.Find<T>. It must be diagnosed inside an async method.
    [Fact]
    public async Task FindOnDbSet_InAsyncMethod_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SyncOverAsyncAnalyzer>("""
            public class Sample
            {
                public async Task<Blog> M(BlogContext db)
                {
                    return db.Blogs.{|EFL2001:Find|}(1);
                }
            }
            """);
    }

    [Fact]
    public async Task ToList_InSyncMethod_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SyncOverAsyncAnalyzer>("""
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
    public async Task ToListAsync_AlreadyUsed_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SyncOverAsyncAnalyzer>("""
            public class Sample
            {
                public async Task M(BlogContext db)
                {
                    var blogs = await db.Blogs.ToListAsync();
                }
            }
            """);
    }

    [Fact]
    public async Task ToList_OnInMemoryList_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SyncOverAsyncAnalyzer>("""
            public class Sample
            {
                public async Task M()
                {
                    var items = new List<Blog>();
                    var copy = items.ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task ToList_InNonAsyncLambda_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SyncOverAsyncAnalyzer>("""
            public class Sample
            {
                public async Task M(BlogContext db)
                {
                    var actions = new List<System.Action>();
                    actions.Add(() => { var blogs = db.Blogs.ToList(); });
                }
            }
            """);
    }

    [Fact]
    public async Task SaveChangesAsync_AlreadyUsed_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SyncOverAsyncAnalyzer>("""
            public class Sample
            {
                public async Task M(BlogContext db)
                {
                    await db.SaveChangesAsync();
                }
            }
            """);
    }

    [Fact]
    public async Task UserDefinedToList_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SyncOverAsyncAnalyzer>("""
            public class FakeQuery
            {
                public object ToList() => new object();
            }

            public class Sample
            {
                public async Task M(FakeQuery query)
                {
                    var value = query.ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task ToList_OnLinqToObjectsQueryable_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<SyncOverAsyncAnalyzer>("""
            public class Sample
            {
                public async Task M()
                {
                    var blogs = new[] { new Blog() }.AsQueryable().ToList();
                }
            }
            """);
    }
}
