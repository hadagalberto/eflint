using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Performance;

public class LoadToDeleteUpdateAnalyzerTests
{
    [Fact]
    public async Task FirstThenRemove_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LoadToDeleteUpdateAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db, int id)
                {
                    var b = db.Blogs.First(x => x.Id == id);
                    db.Blogs.{|EFL1008:Remove|}(b);
                }
            }
            """);
    }

    [Fact]
    public async Task SingleThenRemove_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LoadToDeleteUpdateAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db, int id)
                {
                    var b = db.Blogs.Single(x => x.Id == id);
                    db.Blogs.{|EFL1008:Remove|}(b);
                }
            }
            """);
    }

    [Fact]
    public async Task FindThenRemove_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LoadToDeleteUpdateAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db, int id)
                {
                    var b = db.Blogs.Find(id);
                    db.Blogs.{|EFL1008:Remove|}(b);
                }
            }
            """);
    }

    [Fact]
    public async Task FirstOrDefaultThenUpdate_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LoadToDeleteUpdateAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db, int id)
                {
                    var b = db.Blogs.FirstOrDefault(x => x.Id == id);
                    db.Blogs.{|EFL1008:Update|}(b);
                }
            }
            """);
    }

    [Fact]
    public async Task WhereThenFirstThenRemove_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LoadToDeleteUpdateAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db, int id)
                {
                    var b = db.Blogs.Where(x => x.Id == id).First();
                    db.Blogs.{|EFL1008:Remove|}(b);
                }
            }
            """);
    }

    [Fact]
    public async Task FindThenPropertyAssignment_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LoadToDeleteUpdateAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db, int id)
                {
                    var b = db.Blogs.Find(id);
                    b.{|EFL1008:Name|} = "x";
                    db.SaveChanges();
                }
            }
            """);
    }

    [Fact]
    public async Task FirstThenPropertyAssignment_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LoadToDeleteUpdateAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db, int id)
                {
                    var b = db.Blogs.First(x => x.Id == id);
                    b.{|EFL1008:Name|} = "x";
                    db.SaveChanges();
                }
            }
            """);
    }

    [Fact]
    public async Task ExecuteDelete_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LoadToDeleteUpdateAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db, int id)
                {
                    db.Blogs.Where(x => x.Id == id).ExecuteDelete();
                }
            }
            """);
    }

    [Fact]
    public async Task NewEntityInlineRemove_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LoadToDeleteUpdateAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db, int id)
                {
                    db.Blogs.Remove(new Blog { Id = id });
                }
            }
            """);
    }

    [Fact]
    public async Task NewLocalRemove_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LoadToDeleteUpdateAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var b = new Blog();
                    db.Blogs.Remove(b);
                }
            }
            """);
    }

    [Fact]
    public async Task ParameterRemove_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LoadToDeleteUpdateAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db, Blog b)
                {
                    db.Blogs.Remove(b);
                }
            }
            """);
    }

    [Fact]
    public async Task ReadOnlyFirst_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LoadToDeleteUpdateAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var b = db.Blogs.First();
                    Console.WriteLine(b.Name);
                }
            }
            """);
    }

    [Fact]
    public async Task NewEntityUpdate_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LoadToDeleteUpdateAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    db.Blogs.Update(new Blog());
                }
            }
            """);
    }

    [Fact]
    public async Task NonDbSetRemove_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LoadToDeleteUpdateAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var b = db.Blogs.First();
                    new List<Blog>().Remove(b);
                }
            }
            """);
    }

    [Fact]
    public async Task PropertyAssignmentOnNewEntity_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LoadToDeleteUpdateAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var b = new Blog();
                    b.Name = "x";
                    db.SaveChanges();
                }
            }
            """);
    }

    [Fact]
    public async Task PropertyReadOnLoadedEntity_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<LoadToDeleteUpdateAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db, int id)
                {
                    var b = db.Blogs.Find(id);
                    Console.WriteLine(b.Name);
                }
            }
            """);
    }
}
