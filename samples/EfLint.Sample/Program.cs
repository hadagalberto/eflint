using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EfLint.Sample;

public sealed class Blog
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<Post> Posts { get; set; } = new();

    public List<Tag> Tags { get; set; } = new();

    public Author Owner { get; set; } = new();
}

public sealed class Post
{
    public int Id { get; set; }

    public int BlogId { get; set; }
}

public sealed class Tag
{
    public int Id { get; set; }
}

public sealed class Author
{
    public int Id { get; set; }
}

public sealed class BlogContext : DbContext
{
    public DbSet<Blog> Blogs => Set<Blog>();

    public DbSet<Post> Posts => Set<Post>();
}

/// <summary>
/// Every public method below intentionally violates exactly one EfLint rule. The
/// integration test builds this project and asserts the complete diagnostic set.
/// </summary>
public static class Program
{
    public static void Main()
    {
        Console.WriteLine("EfLint sample: intentionally triggers every rule.");
    }

    // EFL3001 — raw SQL built from an interpolated value.
    public static IQueryable<Blog> UnsafeInterpolation(BlogContext context, string search) =>
        context.Blogs.FromSqlRaw($"SELECT * FROM Blogs WHERE Name = {search}");

    // EFL3001 — raw SQL built by concatenating a non-constant value.
    public static int UnsafeConcatenation(BlogContext context, string table) =>
        context.Database.ExecuteSqlRaw("DELETE FROM " + table);

    // EFL3002 — interpolated SQL flattened into a string local before a raw SQL API.
    public static IQueryable<int> FlattenedInterpolation(BlogContext context, string search)
    {
        string sql = $"SELECT Id FROM Blogs WHERE Name = {search}";
        return context.Database.SqlQueryRaw<int>(sql);
    }

    // EFL1001 — materialized before a later query operator.
    public static List<Blog> EarlyMaterialization(BlogContext context) =>
        context.Blogs.AsNoTracking().ToList().Where(blog => blog.Id > 0).ToList();

    // EFL1002 — Count() compared with zero instead of Any().
    public static bool CountComparison(BlogContext context) =>
        context.Blogs.AsNoTracking().Count() > 0;

    // EFL1003 — read-only query without AsNoTracking().
    public static List<Blog> MissingAsNoTracking(BlogContext context) =>
        context.Blogs.ToList();

    // EFL1004 — navigation accessed per item without being included.
    public static int NavigationInLoop(BlogContext context)
    {
        var total = 0;
        foreach (var blog in context.Blogs.AsNoTracking().ToList())
        {
            total += blog.Posts.Count;
        }

        return total;
    }

    // EFL1005 — First() without OrderBy.
    public static Blog FirstWithoutOrderBy(BlogContext context) =>
        context.Blogs.AsNoTracking().First();

    // EFL1006 — Skip() without OrderBy.
    public static List<Blog> PaginationWithoutOrderBy(BlogContext context) =>
        context.Blogs.AsNoTracking().Skip(5).ToList();

    // EFL1007 — SaveChanges() inside a loop.
    public static void SaveChangesInLoop(BlogContext context)
    {
        foreach (var blog in context.Blogs.AsNoTracking().ToList())
        {
            context.Posts.Add(new Post { BlogId = 1 });
            context.SaveChanges();
        }
    }

    // EFL1008 — entity loaded only to be deleted.
    public static void LoadToDelete(BlogContext context)
    {
        var blog = context.Blogs.AsNoTracking().OrderBy(b => b.Id).First();
        context.Blogs.Remove(blog);
    }

    // EFL1009 — large in-memory collection used with Contains in a query.
    public static List<Blog> LargeContains(BlogContext context)
    {
        var ids = new[] { 1, 2, 3, 4 };
        return context.Blogs.AsNoTracking().Where(blog => ids.Contains(blog.Id)).ToList();
    }

    // EFL1010 — two collection Includes without AsSplitQuery().
    public static List<Blog> MultipleIncludes(BlogContext context) =>
        context.Blogs
            .AsNoTracking()
            .Include(blog => blog.Posts)
            .Include(blog => blog.Tags)
            .ToList();

    // EFL2001 — synchronous materializer inside an async method.
    public static async Task SyncOverAsync(BlogContext context)
    {
        await Task.CompletedTask;
        _ = context.Blogs.AsNoTracking().ToList();
    }

    // EFL2002 — async EF call ignoring an in-scope CancellationToken.
    public static async Task MissingCancellationToken(BlogContext context, CancellationToken cancellationToken) =>
        await context.Blogs.AsNoTracking().ToListAsync();

    // EFL2003 — user method that cannot be translated inside a query lambda.
    public static List<Blog> ClientEvaluation(BlogContext context) =>
        context.Blogs.AsNoTracking().Where(blog => IsGoodName(blog.Name)).ToList();

    // EFL2004 — one context instance captured by a Parallel.ForEach lambda.
    public static void ContextAcrossThreads(BlogContext context)
    {
        Parallel.ForEach(new[] { 1, 2 }, _ => _ = context.Blogs.AsNoTracking().Count());
    }

    // EFL2005 — DbContext registered with a singleton lifetime.
    public static void SingletonContext(IServiceCollection services) =>
        services.AddSingleton<BlogContext>();

    // EFL2006 — Include discarded by a Select projection.
    public static List<string> IncludeWithProjection(BlogContext context) =>
        context.Blogs.AsNoTracking().Include(blog => blog.Posts).Select(blog => blog.Name).ToList();

    // EFL2007 — non-sargable string comparison inside a query.
    public static List<Blog> StringComparisonInQuery(BlogContext context, string name) =>
        context.Blogs.AsNoTracking().Where(blog => blog.Name.ToLower() == name).ToList();

    // EFL2008 — Update on an entity already tracked by the same context.
    public static void RedundantUpdateAttach(BlogContext context)
    {
        var blog = context.Blogs.AsNoTracking().OrderBy(b => b.Id).First();
        context.Update(blog);
    }

    private static bool IsGoodName(string name) => name.Length > 3;
}
