using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Integration;

[Collection(PackedPackageCollection.Name)]
public class PackageTests
{
    [Fact]
    public async Task Pack_ProducesAnalyzerPackageWithExpectedLayout()
    {
        var repositoryRoot = DotNetRunner.FindRepositoryRoot();
        var workspace = PackedPackageHarness.CreateWorkspace("eflint-package-tests");
        try
        {
            var packageDirectory = await PackedPackageHarness.PackAsync(workspace, repositoryRoot);
            var packagePath = Directory.GetFiles(packageDirectory, "*.nupkg").Single();

            using (var archive = ZipFile.OpenRead(packagePath))
            {
                var entries = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);

                Assert.Contains("analyzers/dotnet/cs/EfLint.dll", entries);
                Assert.Contains("analyzers/dotnet/cs/EfLint.CodeFixes.dll", entries);
                Assert.Contains("build/EfLint.props", entries);
                Assert.Contains("README.md", entries);
                Assert.Contains("EfLint.nuspec", entries);
            }

            var shippedAnalyzer = Path.Combine(repositoryRoot, "src", "EfLint", "bin", "Release", "netstandard2.0", "EfLint.dll");
            using (var archive = ZipFile.OpenRead(packagePath))
            {
                var entry = archive.GetEntry("analyzers/dotnet/cs/EfLint.dll");
                Assert.NotNull(entry);
                using var stream = entry!.Open();
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                Assert.True(
                    memory.ToArray().SequenceEqual(File.ReadAllBytes(shippedAnalyzer)),
                    "The packed analyzer DLL is stale: rebuild before packing.");
            }

            await AssertPackageCanBeConsumedAsync(workspace, packagePath);
        }
        finally
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    private static async Task AssertPackageCanBeConsumedAsync(string workspace, string packagePath)
    {
        var output = await PackedPackageHarness.CompileWithPackedAnalyzerAsync(
            workspace,
            "consumer",
            packagePath,
            new Dictionary<string, string>
            {
                ["Sample.cs"] = """
                    using Microsoft.EntityFrameworkCore;

                    public class Post { public int Id { get; set; } }

                    public class BlogContext : DbContext
                    {
                        public DbSet<Post> Posts => Set<Post>();
                    }

                    public static class Sample
                    {
                        public static void Main() { }

                        public static void Query(BlogContext db, string search)
                        {
                            db.Posts.FromSqlRaw($"SELECT * FROM Posts WHERE Name = {search}");
                        }
                    }
                    """,
            });

        Assert.Contains("EFL3001", output);
    }
}
