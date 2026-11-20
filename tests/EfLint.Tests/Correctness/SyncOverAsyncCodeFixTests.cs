using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.CodeFixes.Correctness;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Correctness;

public class SyncOverAsyncCodeFixTests
{
    [Fact]
    public async Task ToList_NoToken_Fixed()
    {
        await EfLintTestResources.VerifyCodeFixAsync<SyncOverAsyncAnalyzer, SyncOverAsyncCodeFixProvider>(
            """
            public class Sample
            {
                public async Task M(BlogContext db)
                {
                    var blogs = db.Blogs.{|EFL2001:ToList|}();
                }
            }
            """,
            """
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
    public async Task Count_TokenInScope_PassesToken()
    {
        await EfLintTestResources.VerifyCodeFixAsync<SyncOverAsyncAnalyzer, SyncOverAsyncCodeFixProvider>(
            """
            public class Sample
            {
                public async Task<int> M(BlogContext db, CancellationToken ct)
                {
                    return db.Blogs.{|EFL2001:Count|}();
                }
            }
            """,
            """
            public class Sample
            {
                public async Task<int> M(BlogContext db, CancellationToken ct)
                {
                    return await db.Blogs.CountAsync(ct);
                }
            }
            """);
    }

    [Fact]
    public async Task SaveChanges_TokenInScope_PassesToken()
    {
        await EfLintTestResources.VerifyCodeFixAsync<SyncOverAsyncAnalyzer, SyncOverAsyncCodeFixProvider>(
            """
            public class Sample
            {
                public async Task M(BlogContext db, CancellationToken ct)
                {
                    db.{|EFL2001:SaveChanges|}();
                }
            }
            """,
            """
            public class Sample
            {
                public async Task M(BlogContext db, CancellationToken ct)
                {
                    await db.SaveChangesAsync(ct);
                }
            }
            """);
    }

    [Fact]
    public async Task ChainedAny_NoToken_Fixed()
    {
        await EfLintTestResources.VerifyCodeFixAsync<SyncOverAsyncAnalyzer, SyncOverAsyncCodeFixProvider>(
            """
            public class Sample
            {
                public async Task<bool> M(BlogContext db)
                {
                    return db.Blogs.Where(b => b.Id > 0).{|EFL2001:Any|}();
                }
            }
            """,
            """
            public class Sample
            {
                public async Task<bool> M(BlogContext db)
                {
                    return await db.Blogs.Where(b => b.Id > 0).AnyAsync();
                }
            }
            """);
    }

    [Fact]
    public async Task Find_GenericCall_Fixed()
    {
        await EfLintTestResources.VerifyCodeFixAsync<SyncOverAsyncAnalyzer, SyncOverAsyncCodeFixProvider>(
            """
            public class Sample
            {
                public async Task<Blog> M(BlogContext db)
                {
                    return db.{|EFL2001:Find<Blog>|}(1);
                }
            }
            """,
            """
            public class Sample
            {
                public async Task<Blog> M(BlogContext db)
                {
                    return await db.FindAsync<Blog>(new object[] { 1 });
                }
            }
            """);
    }

    [Fact]
    public async Task Find_WithToken_Fixed()
    {
        await EfLintTestResources.VerifyCodeFixAsync<SyncOverAsyncAnalyzer, SyncOverAsyncCodeFixProvider>(
            """
            public class Sample
            {
                public async Task<Blog> M(BlogContext db, CancellationToken ct)
                {
                    return db.{|EFL2001:Find<Blog>|}(1);
                }
            }
            """,
            """
            public class Sample
            {
                public async Task<Blog> M(BlogContext db, CancellationToken ct)
                {
                    return await db.FindAsync<Blog>(new object[] { 1 }, ct);
                }
            }
            """);
    }

    // Hand-derived: DbSet<T>.FindAsync(params object[] keyValues) is the direct async
    // counterpart of DbSet<T>.Find, so the fixed code is `await db.Blogs.FindAsync(1)`
    // and must compile against the real EF Core 8 references.
    [Fact]
    public async Task FindOnDbSet_NoToken_Fixed()
    {
        await EfLintTestResources.VerifyCodeFixAsync<SyncOverAsyncAnalyzer, SyncOverAsyncCodeFixProvider>(
            """
            public class Sample
            {
                public async Task<Blog> M(BlogContext db)
                {
                    return db.Blogs.{|EFL2001:Find|}(1);
                }
            }
            """,
            """
            public class Sample
            {
                public async Task<Blog> M(BlogContext db)
                {
                    return await db.Blogs.FindAsync(1);
                }
            }
            """);
    }

    [Fact]
    public async Task FixAll_MultipleCalls_Fixed()
    {
        await EfLintTestResources.VerifyFixAllAsync<SyncOverAsyncAnalyzer, SyncOverAsyncCodeFixProvider>(
            """
            public class Sample
            {
                public async Task M(BlogContext db, CancellationToken ct)
                {
                    var blogs = db.Blogs.{|EFL2001:ToList|}();
                    var count = db.Blogs.{|EFL2001:Count|}();
                }
            }
            """,
            """
            public class Sample
            {
                public async Task M(BlogContext db, CancellationToken ct)
                {
                    var blogs = await db.Blogs.ToListAsync(ct);
                    var count = await db.Blogs.CountAsync(ct);
                }
            }
            """);
    }
}
