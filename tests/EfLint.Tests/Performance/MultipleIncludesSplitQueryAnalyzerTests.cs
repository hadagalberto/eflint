using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Performance;

public class MultipleIncludesSplitQueryAnalyzerTests
{
    private const string Model = """
        public class Post { public int Id { get; set; } }
        public class Tag { public int Id { get; set; } }
        public class Owner { public int Id { get; set; } }

        public class BlogWithNavs
        {
            public int Id { get; set; }
            public List<Post> Posts { get; set; } = new();
            public List<Tag> Tags { get; set; } = new();
            public Owner Owner { get; set; } = new();
        }

        public class BloggingContext : DbContext
        {
            public DbSet<BlogWithNavs> Blogs => Set<BlogWithNavs>();
        }

        """;

    [Fact]
    public async Task TwoCollectionIncludes_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var blogs = db.Blogs.Include(b => b.Posts).{|EFL1010:Include|}(b => b.Tags).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task IncludesWithWhereAfter_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var blogs = db.Blogs.Include(b => b.Posts).{|EFL1010:Include|}(b => b.Tags).Where(b => b.Id > 0).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task StringIncludes_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var blogs = db.Blogs.Include("Posts").{|EFL1010:Include|}("Tags").ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task IncludeAfterWhere_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var blogs = db.Blogs.Include(b => b.Posts).Where(b => b.Id > 0).{|EFL1010:Include|}(b => b.Tags).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task IncludesAfterAsNoTrackingWithFirst_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var blog = db.Blogs.AsNoTracking().Include(b => b.Posts).{|EFL1010:Include|}(b => b.Tags).First();
                }
            }
            """);
    }

    [Fact]
    public async Task IncludesBeforeCount_Reports()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var count = db.Blogs.Include(b => b.Posts).{|EFL1010:Include|}(b => b.Tags).Count();
                }
            }
            """);
    }

    [Fact]
    public async Task ReferenceIncludeAfterCollectionIncludes_ReportsOnLastCollection()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var blogs = db.Blogs.Include(b => b.Posts).{|EFL1010:Include|}(b => b.Tags).Include(b => b.Owner).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task AsSplitQuery_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var blogs = db.Blogs.Include(b => b.Posts).Include(b => b.Tags).AsSplitQuery().ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task SingleCollectionInclude_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var blogs = db.Blogs.Include(b => b.Posts).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task NoIncludes_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var blogs = db.Blogs.ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task OnlyOneCollectionIncludeAmongReferenceIncludes_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var blogs = db.Blogs.Include(b => b.Owner).Include(b => b.Tags).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task NonEfQuery_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M()
                {
                    var blogs = new List<BlogWithNavs>().AsQueryable().Include(b => b.Posts).Include(b => b.Tags).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task AsSingleQuery_NoDiagnostic()
    {
        await VerifyAsync("""
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var blogs = db.Blogs.Include(b => b.Posts).AsSingleQuery().Include(b => b.Tags).ToList();
                }
            }
            """);
    }

    private static Task VerifyAsync(string code) =>
        EfLintTestResources.VerifyAnalyzerAsync<MultipleIncludesSplitQueryAnalyzer>(Model + code);
}
