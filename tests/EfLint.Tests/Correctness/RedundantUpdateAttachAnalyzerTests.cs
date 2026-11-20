using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Correctness;

public class RedundantUpdateAttachAnalyzerTests
{
    [Fact]
    public async Task FirstThenUpdate_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<RedundantUpdateAttachAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blog = db.Blogs.First();
                    db.{|EFL2008:Update|}(blog);
                }
            }
            """);
    }

    [Fact]
    public async Task FirstOrDefaultThenAttach_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<RedundantUpdateAttachAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blog = db.Blogs.Where(b => b.Id > 0).FirstOrDefault();
                    db.{|EFL2008:Attach|}(blog);
                }
            }
            """);
    }

    [Fact]
    public async Task DirectQueryResult_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<RedundantUpdateAttachAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    db.{|EFL2008:Update|}(db.Blogs.First());
                }
            }
            """);
    }

    [Fact]
    public async Task AsyncFirstThenUpdate_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<RedundantUpdateAttachAnalyzer>("""
            public class Sample
            {
                public async Task M(BlogContext db)
                {
                    var blog = await db.Blogs.FirstAsync();
                    db.{|EFL2008:Update|}(blog);
                }
            }
            """);
    }

    [Fact]
    public async Task SingleThenAttach_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<RedundantUpdateAttachAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blog = db.Blogs.Single();
                    db.{|EFL2008:Attach|}(blog);
                }
            }
            """);
    }

    [Fact]
    public async Task OrderedFirstThenUpdate_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<RedundantUpdateAttachAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blog = db.Blogs.OrderBy(b => b.Id).First();
                    db.{|EFL2008:Update|}(blog);
                }
            }
            """);
    }

    [Fact]
    public async Task SetThenUpdate_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<RedundantUpdateAttachAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blog = db.Set<Blog>().First();
                    db.{|EFL2008:Update|}(blog);
                }
            }
            """);
    }

    [Fact]
    public async Task ReassignmentThenUpdate_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<RedundantUpdateAttachAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blog = new Blog();
                    blog = db.Blogs.First();
                    db.{|EFL2008:Update|}(blog);
                }
            }
            """);
    }

    [Fact]
    public async Task NewEntity_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<RedundantUpdateAttachAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blog = new Blog();
                    db.Update(blog);
                }
            }
            """);
    }

    [Fact]
    public async Task DifferentContexts_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<RedundantUpdateAttachAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db, BlogContext other)
                {
                    var blog = db.Blogs.First();
                    other.Update(blog);
                }
            }
            """);
    }

    [Fact]
    public async Task NewEntityInline_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<RedundantUpdateAttachAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    db.Update(new Blog());
                }
            }
            """);
    }

    [Fact]
    public async Task MaterializedListElement_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<RedundantUpdateAttachAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blogs = db.Blogs.ToList();
                    db.Update(blogs[0]);
                }
            }
            """);
    }

    [Fact]
    public async Task UpdateRange_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<RedundantUpdateAttachAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var blog = db.Blogs.First();
                    db.UpdateRange(blog);
                }
            }
            """);
    }

    [Fact]
    public async Task LookAlikeUpdate_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<RedundantUpdateAttachAnalyzer>("""
            public class FakeContext
            {
                public void Update(object entity) { }
            }

            public class Sample
            {
                public void M(BlogContext db, FakeContext fake)
                {
                    var blog = db.Blogs.First();
                    fake.Update(blog);
                }
            }
            """);
    }

    [Fact]
    public async Task UnrelatedQuery_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<RedundantUpdateAttachAnalyzer>("""
            public class Sample
            {
                public void M(BlogContext db)
                {
                    var first = db.Blogs.First();
                    var second = db.Blogs.Skip(1).First();
                    db.Update(new Blog());
                }
            }
            """);
    }
}
