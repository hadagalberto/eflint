using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Correctness;

public class MissingCancellationTokenAnalyzerTests
{
    [Fact]
    public async Task ToListAsync_WithTokenParameter_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<MissingCancellationTokenAnalyzer>("""
            public class Sample
            {
                public async Task M(BlogContext db, CancellationToken ct)
                {
                    var blogs = await db.Blogs.{|EFL2002:ToListAsync|}();
                }
            }
            """);
    }

    [Fact]
    public async Task CountAsync_WithTokenParameter_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<MissingCancellationTokenAnalyzer>("""
            public class Sample
            {
                public async Task<int> M(BlogContext db, CancellationToken ct)
                {
                    return await db.Blogs.{|EFL2002:CountAsync|}();
                }
            }
            """);
    }

    [Fact]
    public async Task FirstOrDefaultAsync_WithTokenParameter_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<MissingCancellationTokenAnalyzer>("""
            public class Sample
            {
                public async Task<Blog> M(BlogContext db, CancellationToken ct)
                {
                    return await db.Blogs.{|EFL2002:FirstOrDefaultAsync|}();
                }
            }
            """);
    }

    [Fact]
    public async Task SaveChangesAsync_WithTokenParameter_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<MissingCancellationTokenAnalyzer>("""
            public class Sample
            {
                public async Task M(BlogContext db, CancellationToken ct)
                {
                    await db.{|EFL2002:SaveChangesAsync|}();
                }
            }
            """);
    }

    [Fact]
    public async Task ExecuteDeleteAsync_WithTokenParameter_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<MissingCancellationTokenAnalyzer>("""
            public class Sample
            {
                public async Task M(BlogContext db, CancellationToken ct)
                {
                    await db.Blogs.{|EFL2002:ExecuteDeleteAsync|}();
                }
            }
            """);
    }

    [Fact]
    public async Task AnyAsync_WithLocalToken_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<MissingCancellationTokenAnalyzer>("""
            public class Sample
            {
                public async Task<bool> M(BlogContext db)
                {
                    var ct = CancellationToken.None;
                    return await db.Blogs.{|EFL2002:AnyAsync|}();
                }
            }
            """);
    }

    [Fact]
    public async Task ToListAsync_InLambdaWithToken_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<MissingCancellationTokenAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    System.Func<CancellationToken, Task> action = async ct =>
                    {
                        var blogs = await db.Blogs.{|EFL2002:ToListAsync|}();
                    };
                }
            }
            """);
    }

    [Fact]
    public async Task ToListAsync_WithPredicateAndTokenParameter_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<MissingCancellationTokenAnalyzer>("""
            public class Sample
            {
                public async Task<bool> M(BlogContext db, CancellationToken ct)
                {
                    return await db.Blogs.{|EFL2002:AnyAsync|}(b => b.Id > 0);
                }
            }
            """);
    }

    [Fact]
    public async Task ToListAsync_WithoutTokenInScope_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<MissingCancellationTokenAnalyzer>("""
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
    public async Task ToListAsync_TokenAlreadyPassed_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<MissingCancellationTokenAnalyzer>("""
            public class Sample
            {
                public async Task M(BlogContext db, CancellationToken ct)
                {
                    var blogs = await db.Blogs.ToListAsync(ct);
                }
            }
            """);
    }

    [Fact]
    public async Task SaveChangesAsync_ExplicitNoneToken_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<MissingCancellationTokenAnalyzer>("""
            public class Sample
            {
                public async Task M(BlogContext db, CancellationToken ct)
                {
                    await db.SaveChangesAsync(CancellationToken.None);
                }
            }
            """);
    }

    [Fact]
    public async Task SyncCall_WithTokenInScope_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<MissingCancellationTokenAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db, CancellationToken ct)
                {
                    var blogs = db.Blogs.ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task TaskDelay_WithTokenInScope_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<MissingCancellationTokenAnalyzer>("""
            public class Sample
            {
                public async Task M(CancellationToken ct)
                {
                    await Task.Delay(1);
                }
            }
            """);
    }

    [Fact]
    public async Task TokenDeclaredInSiblingBlock_NoDiagnostic()
    {
        // Hand-derived: `ct` is declared inside a closed `if` block, so it is not in
        // scope at the ToListAsync call. Passing it would not compile (CS0103).
        await EfLintTestResources.VerifyAnalyzerAsync<MissingCancellationTokenAnalyzer>("""
            public class Sample
            {
                public async Task M(BlogContext db)
                {
                    if (true)
                    {
                        var ct = CancellationToken.None;
                    }
                    var blogs = await db.Blogs.ToListAsync();
                }
            }
            """);
    }

    [Fact]
    public async Task TokenDeclaredInSiblingLoop_NoDiagnostic()
    {
        // Hand-derived: a token declared in an earlier loop body is out of scope
        // after the loop, so the code fix would produce uncompilable code.
        await EfLintTestResources.VerifyAnalyzerAsync<MissingCancellationTokenAnalyzer>("""
            public class Sample
            {
                public async Task M(BlogContext db)
                {
                    for (int i = 0; i < 1; i++)
                    {
                        var ct = CancellationToken.None;
                    }
                    var blogs = await db.Blogs.ToListAsync();
                }
            }
            """);
    }

    [Fact]
    public async Task TokenDeclaredInEnclosingBlock_Reports()
    {
        // Hand-derived: `ct` is declared in a block that still encloses the call,
        // so it is in scope and the fix should be offered.
        await EfLintTestResources.VerifyAnalyzerAsync<MissingCancellationTokenAnalyzer>("""
            public class Sample
            {
                public async Task M(BlogContext db)
                {
                    var ct = CancellationToken.None;
                    if (true)
                    {
                        var blogs = await db.Blogs.{|EFL2002:ToListAsync|}();
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task TokenDeclaredAfterUse_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<MissingCancellationTokenAnalyzer>("""
            public class Sample
            {
                public async Task M(BlogContext db)
                {
                    var blogs = await db.Blogs.ToListAsync();
                    var ct = CancellationToken.None;
                }
            }
            """);
    }

    [Fact]
    public async Task NonTokenLocalInScope_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<MissingCancellationTokenAnalyzer>("""
            public class Sample
            {
                public async Task M(BlogContext db)
                {
                    var ct = 42;
                    var blogs = await db.Blogs.ToListAsync();
                }
            }
            """);
    }

    [Fact]
    public async Task FindAsync_ParamsOnlyOverload_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<MissingCancellationTokenAnalyzer>("""
            public class Sample
            {
                public async Task<Blog> M(BlogContext db, CancellationToken ct)
                {
                    return await db.FindAsync<Blog>(1);
                }
            }
            """);
    }

    [Fact]
    public async Task UserDefinedToListAsync_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<MissingCancellationTokenAnalyzer>("""
            public class FakeQuery
            {
                public Task<object> ToListAsync(CancellationToken ct = default) => Task.FromResult<object>(new object());
            }

            public class Sample
            {
                public async Task M(FakeQuery query, CancellationToken ct)
                {
                    var value = await query.ToListAsync();
                }
            }
            """);
    }
}
