using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.FixedCode;

/// <summary>
/// Every code fix must produce a document that compiles against the real EF Core
/// reference assemblies. Each case below is the hand-written expected output of
/// one of the code fixes (or the untouched source where a fix must not be
/// offered); compilation errors are asserted to be exactly zero.
/// </summary>
public class FixedCodeCompilationTests
{
    private const string Preamble = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;

        public class Blog
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public List<Post> Posts { get; set; } = new();
        }

        public class Post
        {
            public int Id { get; set; }
            public string Title { get; set; } = string.Empty;
        }

        public class BlogContext : DbContext
        {
            public DbSet<Blog> Blogs => Set<Blog>();
            public DbSet<Post> Posts => Set<Post>();
        }

        """;

    public static IEnumerable<object[]> FixedDocuments()
    {
        // EFL3001: interpolated hole keeps no SQL quotes (FromSql).
        yield return Case("EFL3001_FromSql", Method(
            "db.Blogs.FromSql($\"SELECT * FROM Blogs WHERE Name = {name}\").ToList();"));

        // EFL3001: concatenation rewritten without SQL quotes around the hole.
        yield return Case("EFL3001_ExecuteSql", Method(
            "db.Database.ExecuteSql($\"DELETE FROM Blogs WHERE Name = {name}\");"));

        // EFL1007: field-held context receiver stays in scope after the loop.
        yield return Case("EFL1007_FieldContext", Preamble + """
            public class Sample
            {
                private readonly BlogContext _db = new BlogContext();

                public void M(List<Blog> blogs)
                {
                    foreach (var b in blogs)
                    {
                        _db.Blogs.Add(b);
                    }
                    _db.SaveChanges();
                }
            }
            """);

        // EFL2002: in-scope local token passed to the async call.
        yield return Case("EFL2002_LocalToken", Method(
            "var local = CancellationToken.None;",
            "var blogs = await db.Blogs.ToListAsync(local);"));

        // EFL2001: DbSet.Find converted to awaited FindAsync.
        yield return Case("EFL2001_FindAsync", Method(
            "var blog = await db.Blogs.FindAsync(1);"));

        // EFL1002: Count() > 0 converted to Any()/AnyAsync().
        yield return Case("EFL1002_AnyAsync", Method(
            "var any = await db.Blogs.AnyAsync(ct);"));

        // EFL1001: materialization moved to the end of a translatable chain.
        yield return Case("EFL1001_MaterializationAtEnd", Method(
            "var names = db.Blogs.Where(b => b.Id > 0).Select(b => b.Name).ToList();"));

        // EFL1003: AsNoTracking added to an entity query.
        yield return Case("EFL1003_AsNoTracking", Method(
            "var blogs = db.Blogs.AsNoTracking().ToList();"));

        // EFL2006: Include removed from a projection that actually drops it.
        yield return Case("EFL2006_Projection", Method(
            "var names = db.Blogs.Select(b => b.Name).ToList();"));

        // EFL1010: AsSplitQuery added to a multi-collection Include.
        yield return Case("EFL1010_AsSplitQuery", Method(
            "var blogs = db.Blogs.Include(b => b.Posts).AsSplitQuery().ToList();"));

        // EFL2002 fix must NOT be offered for a token declared in a sibling block:
        // the original document is valid C#, so any change would be a regression.
        yield return Case("EFL2002_SiblingBlock_NoFix", Method(
            "if (true) { var local = CancellationToken.None; }",
            "var blogs = await db.Blogs.ToListAsync();"));

        // EFL1007 fix must NOT be offered when the receiver is a loop-local context.
        yield return Case("EFL1007_LoopLocalContext_NoFix", Method(
            "foreach (var id in ids) { var ctx = new BlogContext(); ctx.SaveChanges(); }"));

        // EFL1003 fix must NOT be offered for scalar projections: this source is
        // valid as-is and adding AsNoTracking() would give CS0452.
        yield return Case("EFL1003_ScalarProjection_NoFix", Method(
            "var counts = db.Blogs.Select(b => b.Posts.Count()).ToList();"));
    }

    [Theory]
    [MemberData(nameof(FixedDocuments))]
    public async Task FixedDocument_HasZeroCompilerErrors(string name, string source)
    {
        var errors = await FixedCodeCompiler.GetErrorsAsync(source);

        Assert.True(
            errors.IsDefaultOrEmpty,
            $"{name} fixed document must compile with zero errors, got: "
            + string.Join(" | ", errors.Select(e => e.ToString())));
    }

    private static object[] Case(string name, string source) => new object[] { name, source };

    private static string Method(params string[] statements)
    {
        var body = string.Join("\n        ", statements);
        return Preamble
            + "public class Sample\n{\n"
            + "    public async Task M(BlogContext db, CancellationToken ct, string name, int[] ids)\n    {\n        "
            + body
            + "\n    }\n}\n";
    }
}
