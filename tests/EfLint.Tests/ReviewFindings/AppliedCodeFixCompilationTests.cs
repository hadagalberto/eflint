using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.CodeFixes.Correctness;
using EfLint.CodeFixes.Performance;
using EfLint.CodeFixes.Security;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.ReviewFindings;

/// <summary>
/// Applies each real code fix and compiles the resulting document against the
/// real EF Core 8 reference assemblies. Expected values are the rules' documented
/// outputs; the compiler, not the analyzer, is the oracle for "compiles".
/// </summary>
public class AppliedCodeFixCompilationTests
{
    private const string Model = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;

        public class Post { public int Id { get; set; } }
        public class Tag { public int Id { get; set; } }

        public class Blog
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public List<Post> Posts { get; set; } = new();
            public List<Tag> Tags { get; set; } = new();
        }

        public class BlogContext : DbContext
        {
            public DbSet<Blog> Blogs => Set<Blog>();
            public DbSet<Post> Posts => Set<Post>();
            public DbSet<Tag> Tags => Set<Tag>();
        }

        """;

    private static string Wrap(string body) => Model + "public class Sample\n{\n" + body + "\n}\n";

    [Fact]
    public Task EFL3001_FixedFromSqlCompiles() => CodeFixProbe.FixAndVerifyAsync<
        RawSqlInjectionAnalyzer, RawSqlToFormattableStringCodeFixProvider>(Wrap("""
        public void M(BlogContext db, string name)
        {
            db.Blogs.FromSqlRaw($"SELECT * FROM Blogs WHERE Name = '{name}'");
        }
        """));

    [Fact]
    public Task EFL1001_FixedMaterializationCompiles() => CodeFixProbe.FixAndVerifyAsync<
        EarlyMaterializationAnalyzer, EarlyMaterializationCodeFixProvider>(Wrap("""
        public void M(BlogContext db)
        {
            var blogs = db.Blogs.ToList().Where(b => b.Id > 0).ToList();
        }
        """));

    [Fact]
    public Task EFL1002_FixedAnyCompiles() => CodeFixProbe.FixAndVerifyAsync<
        CountComparisonAnalyzer, CountComparisonCodeFixProvider>(Wrap("""
        public void M(BlogContext db)
        {
            if (db.Blogs.Count() > 0) { }
        }
        """));

    [Fact]
    public Task EFL1003_FixedAsNoTrackingCompiles() => CodeFixProbe.FixAndVerifyAsync<
        MissingAsNoTrackingAnalyzer, MissingAsNoTrackingCodeFixProvider>(Wrap("""
        public void M(BlogContext db)
        {
            var blogs = db.Blogs.ToList();
        }
        """));

    [Fact]
    public Task EFL2001_FixedDbSetFindAsyncCompiles() => CodeFixProbe.FixAndVerifyAsync<
        SyncOverAsyncAnalyzer, SyncOverAsyncCodeFixProvider>(Wrap("""
        public async Task<Blog> M(BlogContext db)
        {
            await Task.CompletedTask;
            return db.Blogs.Find(1);
        }
        """));

    [Fact]
    public Task EFL2002_FixedLocalTokenCompiles() => CodeFixProbe.FixAndVerifyAsync<
        MissingCancellationTokenAnalyzer, MissingCancellationTokenCodeFixProvider>(Wrap("""
        public async Task M(BlogContext db)
        {
            var ct = CancellationToken.None;
            var blogs = await db.Blogs.ToListAsync();
        }
        """));

    [Fact]
    public Task EFL2006_FixedIncludeRemovalCompiles() => CodeFixProbe.FixAndVerifyAsync<
        IncludeWithProjectionAnalyzer, IncludeWithProjectionCodeFixProvider>(Wrap("""
        public void M(BlogContext db)
        {
            var ids = db.Blogs.Include(b => b.Posts).Select(b => b.Id).ToList();
        }
        """));

    [Fact]
    public Task EFL1007_FixedFieldContextCompiles() => CodeFixProbe.FixAndVerifyAsync<
        SaveChangesInLoopAnalyzer, SaveChangesInLoopCodeFixProvider>(Model + """
        public class Sample
        {
            private readonly BlogContext _db = new BlogContext();

            public void M(List<Blog> blogs)
            {
                foreach (var b in blogs)
                {
                    _db.Blogs.Add(b);
                    _db.SaveChanges();
                }
            }
        }
        """);

    [Fact]
    public Task EFL1010_FixedAsSplitQueryCompiles() => CodeFixProbe.FixAndVerifyAsync<
        MultipleIncludesSplitQueryAnalyzer, MultipleIncludesSplitQueryCodeFixProvider>(Wrap("""
        public void M(BlogContext db)
        {
            var blogs = db.Blogs.Include(b => b.Posts).Include(b => b.Tags).ToList();
        }
        """));
}
