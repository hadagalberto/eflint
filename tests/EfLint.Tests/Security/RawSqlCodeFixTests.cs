using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.CodeFixes.Security;
using EfLint.Tests.TestInfrastructure;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace EfLint.Tests.Security;

public class RawSqlCodeFixTests
{
    [Fact]
    public async Task InterpolatedString_FromSqlRaw_Fixed()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    db.Blogs.FromSqlRaw({|EFL3001:$"SELECT * FROM Blogs WHERE Name = {name}"|});
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    db.Blogs.FromSql($"SELECT * FROM Blogs WHERE Name = {name}");
                }
            }
            """);
    }

    [Fact]
    public async Task Concatenation_ExecuteSqlRaw_Fixed()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    db.Database.ExecuteSqlRaw({|EFL3001:"DELETE FROM Blogs WHERE Name = '" + name + "'"|});
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    db.Database.ExecuteSql($"DELETE FROM Blogs WHERE Name = '{name}'");
                }
            }
            """);
    }

    [Fact]
    public async Task InterpolatedString_ExecuteSqlRawAsync_Fixed()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public async System.Threading.Tasks.Task M(BlogContext db, string name)
                {
                    await db.Database.ExecuteSqlRawAsync({|EFL3001:$"DELETE FROM Blogs WHERE Name = {name}"|});
                }
            }
            """,
            """
            public class Sample
            {
                public async System.Threading.Tasks.Task M(BlogContext db, string name)
                {
                    await db.Database.ExecuteSqlAsync($"DELETE FROM Blogs WHERE Name = {name}");
                }
            }
            """);
    }

    [Fact]
    public async Task SqlQueryRaw_Fixed()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    db.Database.SqlQueryRaw<int>({|EFL3001:$"SELECT Id FROM Blogs WHERE Name = {name}"|});
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    db.Database.SqlQuery<int>($"SELECT Id FROM Blogs WHERE Name = {name}");
                }
            }
            """);
    }

    [Fact]
    public async Task StaticCallWithoutUsing_AddsEntityFrameworkCoreUsing()
    {
        const string before = """
            using System.Linq;

            public class Blog
            {
                public int Id { get; set; }
            }

            public class BlogContext : Microsoft.EntityFrameworkCore.DbContext
            {
                public Microsoft.EntityFrameworkCore.DbSet<Blog> Blogs => Set<Blog>();
            }

            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    Microsoft.EntityFrameworkCore.RelationalQueryableExtensions.FromSqlRaw(db.Blogs, {|EFL3001:$"SELECT {name}"|});
                }
            }
            """;

        const string after = """
            using System.Linq;
            using Microsoft.EntityFrameworkCore;

            public class Blog
            {
                public int Id { get; set; }
            }

            public class BlogContext : Microsoft.EntityFrameworkCore.DbContext
            {
                public Microsoft.EntityFrameworkCore.DbSet<Blog> Blogs => Set<Blog>();
            }

            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    Microsoft.EntityFrameworkCore.RelationalQueryableExtensions.FromSql(db.Blogs, $"SELECT {name}");
                }
            }
            """;

        var test = new CSharpCodeFixTest<RawSqlInjectionAnalyzer, RawSqlToFormattableStringCodeFixProvider, DefaultVerifier>
        {
            ReferenceAssemblies = EfLintTestResources.EfCore,
            TestCode = before,
            FixedCode = after,
        };

        await test.RunAsync();
    }

    [Fact]
    public async Task FixAll_AllDiagnosticsFixed()
    {
        const string before = """
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    db.Blogs.FromSqlRaw({|EFL3001:$"SELECT * FROM Blogs WHERE Name = {name}"|});
                    db.Database.ExecuteSqlRaw({|EFL3001:"DELETE FROM Blogs WHERE Name = '" + name + "'"|});
                }
            }
            """;

        const string after = """
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    db.Blogs.FromSql($"SELECT * FROM Blogs WHERE Name = {name}");
                    db.Database.ExecuteSql($"DELETE FROM Blogs WHERE Name = '{name}'");
                }
            }
            """;

        var test = new CSharpCodeFixTest<RawSqlInjectionAnalyzer, RawSqlToFormattableStringCodeFixProvider, DefaultVerifier>
        {
            ReferenceAssemblies = EfLintTestResources.EfCore,
            TestCode = EfLintTestResources.WithModel(before),
            FixedCode = EfLintTestResources.WithModel(after),
            BatchFixedCode = EfLintTestResources.WithModel(after),
            CodeActionValidationMode = CodeActionValidationMode.None,
            NumberOfFixAllIterations = 1,
            NumberOfFixAllInDocumentIterations = 1,
            NumberOfFixAllInProjectIterations = 1,
            NumberOfIncrementalIterations = 2,
        };

        await test.RunAsync();
    }

    private static Task VerifyAsync(string before, string after)
    {
        var test = new CSharpCodeFixTest<RawSqlInjectionAnalyzer, RawSqlToFormattableStringCodeFixProvider, DefaultVerifier>
        {
            ReferenceAssemblies = EfLintTestResources.EfCore,
            TestCode = EfLintTestResources.WithModel(before),
            FixedCode = EfLintTestResources.WithModel(after),
        };

        return test.RunAsync();
    }
}
