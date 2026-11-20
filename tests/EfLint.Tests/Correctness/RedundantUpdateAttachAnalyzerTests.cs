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

    // Hand-derived: the entity is materialized through _db.Blogs and then handed to
    // _db.Update. Both operations use the same field symbol, so EFL2008 must report.
    [Fact]
    public async Task FieldHeldContext_ThenUpdate_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<RedundantUpdateAttachAnalyzer>("""
            public class Sample
            {
                private readonly BlogContext _db = new BlogContext();

                public void M()
                {
                    var blog = _db.Blogs.First();
                    _db.{|EFL2008:Update|}(blog);
                }
            }
            """);
    }

    // Hand-derived: same field symbol on both sides of the materialize/attach pair.
    [Fact]
    public async Task FieldHeldContext_ThenAttach_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<RedundantUpdateAttachAnalyzer>("""
            public class Sample
            {
                private readonly BlogContext _db = new BlogContext();

                public void M()
                {
                    var blog = _db.Blogs.Where(b => b.Id > 0).FirstOrDefault();
                    _db.{|EFL2008:Attach|}(blog);
                }
            }
            """);
    }

    // Hand-derived: property-backed context is still one context instance; the query
    // root and the Update receiver are the same property symbol on the same `this`.
    [Fact]
    public async Task PropertyHeldContext_ThenUpdate_Reports()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<RedundantUpdateAttachAnalyzer>("""
            public class Sample
            {
                private BlogContext Db { get; } = new BlogContext();

                public void M()
                {
                    var blog = Db.Blogs.First();
                    Db.{|EFL2008:Update|}(blog);
                }
            }
            """);
    }

    // Hand-derived: two distinct fields are two different contexts, so attaching the
    // entity loaded from _db to _other is meaningful and must stay diagnostic-free.
    [Fact]
    public async Task DifferentFieldContexts_NoDiagnostic()
    {
        await EfLintTestResources.VerifyAnalyzerAsync<RedundantUpdateAttachAnalyzer>("""
            public class Sample
            {
                private readonly BlogContext _db = new BlogContext();
                private readonly BlogContext _other = new BlogContext();

                public void M()
                {
                    var blog = _db.Blogs.First();
                    _other.Update(blog);
                }
            }
            """);
    }
}
