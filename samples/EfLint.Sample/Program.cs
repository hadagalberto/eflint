using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace EfLint.Sample;

public sealed class Blog
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class BlogContext : DbContext
{
    public DbSet<Blog> Blogs => Set<Blog>();
}

public static class Program
{
    public static void Main()
    {
        Console.WriteLine("EfLint sample: intentionally triggers EFL3001 and EFL3002.");
    }

    public static IQueryable<Blog> UnsafeInterpolation(BlogContext context, string search)
    {
        return context.Blogs.FromSqlRaw($"SELECT * FROM Blogs WHERE Name = {search}");
    }

    public static int UnsafeConcatenation(BlogContext context, string table)
    {
        return context.Database.ExecuteSqlRaw("DELETE FROM " + table);
    }

    public static IQueryable<int> FlattenedInterpolation(BlogContext context, string search)
    {
        string sql = $"SELECT Id FROM Blogs WHERE Name = {search}";
        return context.Database.SqlQueryRaw<int>(sql);
    }
}
