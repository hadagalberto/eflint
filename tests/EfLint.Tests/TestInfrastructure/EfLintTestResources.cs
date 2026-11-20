using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Testing;

namespace EfLint.Tests.TestInfrastructure;

internal static class EfLintTestResources
{
    public static readonly ReferenceAssemblies EfCore = ReferenceAssemblies.Net.Net80.AddPackages(
        ImmutableArray.Create(new Microsoft.CodeAnalysis.Testing.PackageIdentity("Microsoft.EntityFrameworkCore.Relational", "8.0.11")));

    /// <summary>Minimal EF Core model shared by every analyzer test.</summary>
    public const string Model = """
        using System;
        using System.Linq;
        using Microsoft.EntityFrameworkCore;

        public class Blog
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
        }

        public class BlogContext : DbContext
        {
            public DbSet<Blog> Blogs => Set<Blog>();
        }

        """;

    public static string WithModel(string code) => Model + code;
}
