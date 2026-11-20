using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.Tests.TestInfrastructure;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace EfLint.Tests.Security;

public class FlattenedSqlInterpolationAnalyzerTests
{
    [Fact]
    public async Task StringLocal_FromSqlRaw_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    string sql = $"SELECT * FROM Blogs WHERE Name = {name}";
                    db.Blogs.FromSqlRaw({|EFL3002:sql|});
                }
            }
            """);
    }

    [Fact]
    public async Task VarLocal_ExecuteSqlRaw_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    var sql = $"DELETE FROM Blogs WHERE Name = {name}";
                    db.Database.ExecuteSqlRaw({|EFL3002:sql|});
                }
            }
            """);
    }

    [Fact]
    public async Task ReassignedLocal_SqlQueryRaw_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    string sql = "SELECT 1";
                    sql = $"SELECT Id FROM Blogs WHERE Name = {name}";
                    db.Database.SqlQueryRaw<int>({|EFL3002:sql|});
                }
            }
            """);
    }

    [Fact]
    public async Task ConditionallyAssignedLocal_FromSqlRaw_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db, string name, bool flag)
                {
                    string sql = "SELECT 1";
                    if (flag)
                    {
                        sql = $"SELECT * FROM Blogs WHERE Name = {name}";
                    }

                    db.Blogs.FromSqlRaw({|EFL3002:sql|});
                }
            }
            """);
    }

    [Fact]
    public async Task LocalInsideLambda_ExecuteSqlRaw_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    System.Action action = () =>
                    {
                        string sql = $"DELETE FROM Blogs WHERE Name = {name}";
                        db.Database.ExecuteSqlRaw({|EFL3002:sql|});
                    };
                }
            }
            """);
    }

    [Fact]
    public async Task Local_ExecuteSqlRawAsync_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public async System.Threading.Tasks.Task M(BlogContext db, string name)
                {
                    string sql = $"DELETE FROM Blogs WHERE Name = {name}";
                    await db.Database.ExecuteSqlRawAsync({|EFL3002:sql|});
                }
            }
            """);
    }

    [Fact]
    public async Task TwoLocals_OnlyDangerousOneReported()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    string safe = "SELECT 1";
                    string dangerous = $"SELECT * FROM Blogs WHERE Name = {name}";
                    db.Blogs.FromSqlRaw({|EFL3002:dangerous|});
                    db.Blogs.FromSqlRaw(safe);
                }
            }
            """);
    }

    [Fact]
    public async Task DirectInterpolatedString_FromSqlInterpolated_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    db.Blogs.FromSqlInterpolated($"SELECT * FROM Blogs WHERE Name = {name}");
                }
            }
            """);
    }

    [Fact]
    public async Task FormattableStringLocal_FromSql_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    FormattableString sql = $"SELECT * FROM Blogs WHERE Name = {name}";
                    db.Blogs.FromSql(sql);
                }
            }
            """);
    }

    [Fact]
    public async Task ConstantStringLocal_FromSqlRaw_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    string sql = "SELECT * FROM Blogs";
                    db.Blogs.FromSqlRaw(sql);
                }
            }
            """);
    }

    [Fact]
    public async Task LocalWithConstantInterpolation_FromSqlRaw_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    string sql = $"SELECT * FROM Blogs WHERE Id = {1}";
                    db.Blogs.FromSqlRaw(sql);
                }
            }
            """);
    }

    [Fact]
    public async Task SafeAssignmentAfterUnsafeOne_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    string sql = $"SELECT * FROM Blogs WHERE Name = {name}";
                    sql = "SELECT 1";
                    db.Blogs.FromSqlRaw(sql);
                }
            }
            """);
    }

    [Fact]
    public async Task OtherLocalPassedToRaw_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    string dangerous = $"SELECT * FROM Blogs WHERE Name = {name}";
                    string safe = "SELECT 1";
                    db.Blogs.FromSqlRaw(safe);
                }
            }
            """);
    }

    [Fact]
    public async Task WithoutEntityFrameworkReference_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<FlattenedSqlInterpolationAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                public class FakeDb
                {
                    public void FromSqlRaw(string sql) { }
                }

                public class Sample
                {
                    public void M(FakeDb db, string name)
                    {
                        string sql = $"SELECT {name}";
                        db.FromSqlRaw(sql);
                    }
                }
                """,
        };

        await test.RunAsync();
    }

    private static Task VerifyAsync(string code)
    {
        var test = new CSharpAnalyzerTest<FlattenedSqlInterpolationAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = EfLintTestResources.EfCore,
            TestCode = EfLintTestResources.WithModel(code),
        };

        return test.RunAsync();
    }
}
