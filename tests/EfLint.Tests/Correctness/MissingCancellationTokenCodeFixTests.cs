using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.CodeFixes.Correctness;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Correctness;

public class MissingCancellationTokenCodeFixTests
{
    [Fact]
    public async Task ToListAsync_PassesParameter()
    {
        await EfLintTestResources.VerifyCodeFixAsync<MissingCancellationTokenAnalyzer, MissingCancellationTokenCodeFixProvider>(
            """
            public class Sample
            {
                public async Task M(BlogContext db, CancellationToken ct)
                {
                    var blogs = await db.Blogs.{|EFL2002:ToListAsync|}();
                }
            }
            """,
            """
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
    public async Task CountAsync_PassesParameter()
    {
        await EfLintTestResources.VerifyCodeFixAsync<MissingCancellationTokenAnalyzer, MissingCancellationTokenCodeFixProvider>(
            """
            public class Sample
            {
                public async Task<int> M(BlogContext db, CancellationToken ct)
                {
                    return await db.Blogs.{|EFL2002:CountAsync|}();
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
    public async Task SaveChangesAsync_PassesParameter()
    {
        await EfLintTestResources.VerifyCodeFixAsync<MissingCancellationTokenAnalyzer, MissingCancellationTokenCodeFixProvider>(
            """
            public class Sample
            {
                public async Task M(BlogContext db, CancellationToken ct)
                {
                    await db.{|EFL2002:SaveChangesAsync|}();
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
    public async Task AnyAsync_WithPredicate_AppendsToken()
    {
        await EfLintTestResources.VerifyCodeFixAsync<MissingCancellationTokenAnalyzer, MissingCancellationTokenCodeFixProvider>(
            """
            public class Sample
            {
                public async Task<bool> M(BlogContext db, CancellationToken ct)
                {
                    return await db.Blogs.{|EFL2002:AnyAsync|}(b => b.Id > 0);
                }
            }
            """,
            """
            public class Sample
            {
                public async Task<bool> M(BlogContext db, CancellationToken ct)
                {
                    return await db.Blogs.AnyAsync(b => b.Id > 0, ct);
                }
            }
            """);
    }

    [Fact]
    public async Task ToListAsync_WithLocalToken_PassesLocal()
    {
        await EfLintTestResources.VerifyCodeFixAsync<MissingCancellationTokenAnalyzer, MissingCancellationTokenCodeFixProvider>(
            """
            public class Sample
            {
                public async Task M(BlogContext db)
                {
                    var ct = CancellationToken.None;
                    var blogs = await db.Blogs.{|EFL2002:ToListAsync|}();
                }
            }
            """,
            """
            public class Sample
            {
                public async Task M(BlogContext db)
                {
                    var ct = CancellationToken.None;
                    var blogs = await db.Blogs.ToListAsync(ct);
                }
            }
            """);
    }

    [Fact]
    public async Task FixAll_MultipleOperations_Fixed()
    {
        await EfLintTestResources.VerifyFixAllAsync<MissingCancellationTokenAnalyzer, MissingCancellationTokenCodeFixProvider>(
            """
            public class Sample
            {
                public async Task M(BlogContext db, CancellationToken ct)
                {
                    var blogs = await db.Blogs.{|EFL2002:ToListAsync|}();
                    var count = await db.Blogs.{|EFL2002:CountAsync|}();
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
