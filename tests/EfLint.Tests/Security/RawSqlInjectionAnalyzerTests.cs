using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.Tests.TestInfrastructure;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace EfLint.Tests.Security;

public class RawSqlInjectionAnalyzerTests
{
    [Fact]
    public async Task FromSqlRaw_InterpolatedString_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    db.Blogs.FromSqlRaw({|EFL3001:$"SELECT * FROM Blogs WHERE Name = {name}"|});
                }
            }
            """);
    }

    [Fact]
    public async Task ExecuteSqlRaw_InterpolatedString_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    db.Database.ExecuteSqlRaw({|EFL3001:$"DELETE FROM Blogs WHERE Name = {name}"|});
                }
            }
            """);
    }

    [Fact]
    public async Task SqlQueryRaw_InterpolatedString_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    db.Database.SqlQueryRaw<int>({|EFL3001:$"SELECT Id FROM Blogs WHERE Name = {name}"|});
                }
            }
            """);
    }

    [Fact]
    public async Task FromSqlRaw_StringConcatenation_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    db.Blogs.FromSqlRaw({|EFL3001:"SELECT * FROM Blogs WHERE Name = '" + name + "'"|});
                }
            }
            """);
    }

    [Fact]
    public async Task ExecuteSqlRawAsync_InterpolatedString_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public async System.Threading.Tasks.Task M(BlogContext db, string name)
                {
                    await db.Database.ExecuteSqlRawAsync({|EFL3001:$"DELETE FROM Blogs WHERE Name = {name}"|});
                }
            }
            """);
    }

    [Fact]
    public async Task FromSqlRaw_MethodCallInterpolation_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    db.Blogs.FromSqlRaw({|EFL3001:$"SELECT * FROM Blogs WHERE Name = {GetName()}"|});
                }

                private static string GetName() => "alice";
            }
            """);
    }

    [Fact]
    public async Task ExecuteSqlRaw_StringFormat_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    db.Database.ExecuteSqlRaw({|EFL3001:string.Format("DELETE FROM Blogs WHERE Name = '{0}'", name)|});
                }
            }
            """);
    }

    [Fact]
    public async Task FromSqlRaw_ConcatenationWithNumericValue_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db, string table, int id)
                {
                    db.Blogs.FromSqlRaw({|EFL3001:table + " WHERE Id = " + id|});
                }
            }
            """);
    }

    [Fact]
    public async Task FromSqlRaw_ConstantLiteral_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    db.Blogs.FromSqlRaw("SELECT * FROM Blogs");
                }
            }
            """);
    }

    [Fact]
    public async Task FromSqlRaw_InterpolationWithConstantOnly_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    db.Blogs.FromSqlRaw($"SELECT * FROM Blogs WHERE Id = {1}");
                }
            }
            """);
    }

    [Fact]
    public async Task FromSqlRaw_ConcatenationOfConstants_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    db.Blogs.FromSqlRaw("SELECT * " + "FROM Blogs");
                }
            }
            """);
    }

    [Fact]
    public async Task FromSqlInterpolated_InterpolatedString_NoDiagnostic()
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
    public async Task FromSql_InterpolatedString_NoDiagnostic()
    {
        await VerifyAsync("""
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
    public async Task FromSqlRaw_ParameterPlaceholder_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db, string name)
                {
                    db.Blogs.FromSqlRaw("SELECT * FROM Blogs WHERE Name = {0}", name);
                }
            }
            """);
    }

    [Fact]
    public async Task StringFormat_AllConstants_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    db.Database.ExecuteSqlRaw(string.Format("DELETE FROM Blogs WHERE Name = '{0}'", "alice"));
                }
            }
            """);
    }

    [Fact]
    public async Task LookAlikeApi_NoDiagnostic()
    {
        await VerifyAsync("""
            public class FakeDb
            {
                public void FromSqlRaw(string sql) { }
            }

            public class Sample
            {
                public void M(FakeDb db, string name)
                {
                    db.FromSqlRaw($"SELECT {name}");
                }
            }
            """);
    }

    [Fact]
    public async Task WithoutEntityFrameworkReference_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RawSqlInjectionAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestCode = """
                public class FakeDb
                {
                    public void SqlQueryRaw<T>(string sql) { }
                }

                public class Sample
                {
                    public void M(FakeDb db, string name)
                    {
                        db.SqlQueryRaw<int>($"SELECT {name}");
                    }
                }
                """,
        };

        await test.RunAsync();
    }

    private static Task VerifyAsync(string code)
    {
        var test = new CSharpAnalyzerTest<RawSqlInjectionAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = EfLintTestResources.EfCore,
            TestCode = EfLintTestResources.WithModel(code),
        };

        return test.RunAsync();
    }
}
