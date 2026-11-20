using System.Threading.Tasks;
using EfLint.Analyzers;
using EfLint.CodeFixes.Performance;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Performance;

public class MultipleIncludesSplitQueryCodeFixTests
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
    public async Task TwoCollectionIncludes_AsSplitQueryInserted()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var blogs = db.Blogs.Include(b => b.Posts).{|EFL1010:Include|}(b => b.Tags).ToList();
                }
            }
            """,
            """
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
    public async Task WhereAfterIncludes_AsSplitQueryBeforeWhere()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var blogs = db.Blogs.Include(b => b.Posts).{|EFL1010:Include|}(b => b.Tags).Where(b => b.Id > 0).ToList();
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var blogs = db.Blogs.Include(b => b.Posts).Include(b => b.Tags).AsSplitQuery().Where(b => b.Id > 0).ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task IncludeAfterWhere_AsSplitQueryAfterLastInclude()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var blogs = db.Blogs.Include(b => b.Posts).Where(b => b.Id > 0).{|EFL1010:Include|}(b => b.Tags).ToList();
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var blogs = db.Blogs.Include(b => b.Posts).Where(b => b.Id > 0).Include(b => b.Tags).AsSplitQuery().ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task StringIncludes_AsSplitQueryInserted()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var blogs = db.Blogs.Include("Posts").{|EFL1010:Include|}("Tags").ToList();
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var blogs = db.Blogs.Include("Posts").Include("Tags").AsSplitQuery().ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task AsNoTrackingAndFirst_AsSplitQueryInserted()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var blog = db.Blogs.AsNoTracking().Include(b => b.Posts).{|EFL1010:Include|}(b => b.Tags).First();
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var blog = db.Blogs.AsNoTracking().Include(b => b.Posts).Include(b => b.Tags).AsSplitQuery().First();
                }
            }
            """);
    }

    [Fact]
    public async Task TrailingReferenceInclude_AsSplitQueryAfterLastInclude()
    {
        await VerifyAsync(
            """
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var blogs = db.Blogs.Include(b => b.Posts).{|EFL1010:Include|}(b => b.Tags).Include(b => b.Owner).ToList();
                }
            }
            """,
            """
            public class Sample
            {
                public void M(BloggingContext db)
                {
                    var blogs = db.Blogs.Include(b => b.Posts).Include(b => b.Tags).Include(b => b.Owner).AsSplitQuery().ToList();
                }
            }
            """);
    }

    [Fact]
    public async Task FixAll_TwoChains_AsSplitQueryInsertedInBoth()
    {
        await EfLintTestResources.VerifyFixAllAsync<MultipleIncludesSplitQueryAnalyzer, MultipleIncludesSplitQueryCodeFixProvider>(
            Model + """
                public class Sample
                {
                    public void M(BloggingContext db)
                    {
                        var a = db.Blogs.Include(b => b.Posts).{|EFL1010:Include|}(b => b.Tags).ToList();
                        var c = db.Blogs.Include("Posts").{|EFL1010:Include|}("Tags").ToList();
                    }
                }
                """,
            Model + """
                public class Sample
                {
                    public void M(BloggingContext db)
                    {
                        var a = db.Blogs.Include(b => b.Posts).Include(b => b.Tags).AsSplitQuery().ToList();
                        var c = db.Blogs.Include("Posts").Include("Tags").AsSplitQuery().ToList();
                    }
                }
                """);
    }

    private static Task VerifyAsync(string before, string after) =>
        EfLintTestResources.VerifyCodeFixAsync<MultipleIncludesSplitQueryAnalyzer, MultipleIncludesSplitQueryCodeFixProvider>(
            Model + before,
            Model + after);
}
