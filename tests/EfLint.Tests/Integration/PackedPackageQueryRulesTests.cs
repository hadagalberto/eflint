using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Integration;

/// <summary>
/// Regression coverage for the maintainer report: a fresh <c>dotnet new console</c>
/// project that references the packed nupkg and the real EF Core 8 Sqlite
/// assemblies must report the full diagnostic set, not just EFL3001.
/// </summary>
public class PackedPackageQueryRulesTests
{
    private static readonly Regex Diagnostic = new(
        @"(?<file>[^\\/\r\n: ]+\.cs)\((?<line>\d+),(?<column>\d+)\):\s+warning\s+(?<id>EFL\d{4})\s*:",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const string MaintainerFile = """
        using Microsoft.EntityFrameworkCore;
        public class User { public int Id { get; set; } public string Name { get; set; } = ""; }
        public class Ctx : DbContext { public DbSet<User> Users { get; set; } = null!; protected override void OnConfiguring(DbContextOptionsBuilder o) => o.UseSqlite("Data Source=:memory:"); }
        public class Repo
        {
            public async Task<int> Run(Ctx db, string name, CancellationToken ct)
            {
                var b = db.Users.ToList().Where(u => u.Name == name).ToList();
                if (db.Users.Count() > 0) Console.WriteLine("any");
                foreach (var u in db.Users.ToList()) { u.Name += "!"; db.SaveChanges(); }
                var f = db.Users.FirstOrDefault();
                var page = db.Users.Skip(10).Take(10).ToList();
                await Task.Delay(1, ct);
                return b.Count + page.Count + (f?.Id ?? 0);
            }
            public static void Main() { }
        }
        """;

    private const string AsyncTokenFile = """
        using Microsoft.EntityFrameworkCore;
        public static class AsyncTokenScenario
        {
            public static async Task<int> Run(Ctx db, CancellationToken ct)
            {
                var count = await db.Users.CountAsync();
                await db.SaveChangesAsync();
                return count;
            }
        }
        """;

    [Fact]
    public async Task PackedPackage_ReportsEveryExpectedRuleInRealConsumer()
    {
        var repositoryRoot = DotNetRunner.FindRepositoryRoot();
        var workspace = PackedPackageHarness.CreateWorkspace("eflint-packed-rules");
        try
        {
            var packageDirectory = await PackedPackageHarness.PackAsync(workspace, repositoryRoot);
            var packagePath = Directory.GetFiles(packageDirectory, "*.nupkg").Single();

            await AssertExactMaintainerDiagnosticsAsync(workspace, packagePath);
            await AssertScenarioDiagnosticsAsync(workspace, packagePath, "topLevel", TopLevelFile(), ExpectedMaintainerIds());
            await AssertScenarioDiagnosticsAsync(workspace, packagePath, "expressionBodied", WithContext("public DbSet<User> Users => Set<User>();"), ExpectedMaintainerIds());
            await AssertScenarioDiagnosticsAsync(workspace, packagePath, "field", WithContext("public DbSet<User> Users = null!;"), ExpectedMaintainerIds());

            var withAsync = ExpectedMaintainerIds().Concat(new[] { "EFL2002", "EFL2002" })
                .OrderBy(id => id, StringComparer.Ordinal).ToArray();
            await AssertScenarioDiagnosticsAsync(
                workspace,
                packagePath,
                "asyncToken",
            new Dictionary<string, string> { ["Program.cs"] = MaintainerFile, ["AsyncToken.cs"] = AsyncTokenFile },
            withAsync);
        }
        finally
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    private static async Task AssertExactMaintainerDiagnosticsAsync(string workspace, string packagePath)
    {
        var output = await PackedPackageHarness.CompileWithPackedAnalyzerAsync(
            workspace, "exact", packagePath,
            new Dictionary<string, string> { ["Program.cs"] = MaintainerFile });

        var actual = Parse(output)
            .Select(d => (File: d.File, Line: d.Line, Column: d.Column, Id: d.Id))
            .Distinct()
            .OrderBy(d => d.Line).ThenBy(d => d.Column).ThenBy(d => d.Id, StringComparer.Ordinal)
            .ToArray();

        (string File, int Line, int Column, string Id)[] expected = new[]
        {
            ("Program.cs", 8, 26, "EFL1001"),
            ("Program.cs", 8, 26, "EFL2001"),
            ("Program.cs", 9, 22, "EFL1002"),
            ("Program.cs", 9, 22, "EFL2001"),
            ("Program.cs", 10, 36, "EFL2001"),
            ("Program.cs", 10, 66, "EFL1007"),
            ("Program.cs", 10, 66, "EFL2001"),
            ("Program.cs", 11, 26, "EFL1005"),
            ("Program.cs", 11, 26, "EFL2001"),
            ("Program.cs", 12, 29, "EFL1006"),
            ("Program.cs", 12, 38, "EFL1006"),
            ("Program.cs", 12, 47, "EFL2001"),
        };

        Assert.Equal(expected, actual);
    }

    private static async Task AssertScenarioDiagnosticsAsync(
        string workspace,
        string packagePath,
        string name,
        string program,
        string[] expectedIds) =>
        await AssertScenarioDiagnosticsAsync(
            workspace, packagePath, name,
            new Dictionary<string, string> { ["Program.cs"] = program },
            expectedIds);

    private static async Task AssertScenarioDiagnosticsAsync(
        string workspace,
        string packagePath,
        string name,
        IReadOnlyDictionary<string, string> sources,
        string[] expectedIds)
    {
        var output = await PackedPackageHarness.CompileWithPackedAnalyzerAsync(workspace, name, packagePath, sources);
        var actual = Parse(output).Select(d => (d.File, d.Line, d.Column, d.Id)).Distinct()
            .Select(d => d.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedIds.OrderBy(id => id, StringComparer.Ordinal).ToArray(), actual);
    }

    private static string[] ExpectedMaintainerIds() => new[]
    {
        "EFL1001", "EFL1002", "EFL1005", "EFL1006", "EFL1006", "EFL1007",
        "EFL2001", "EFL2001", "EFL2001", "EFL2001", "EFL2001", "EFL2001",
    };

    private static string WithContext(string context)
    {
        var replace = "public DbSet<User> Users { get; set; } = null!;";
        Assert.Contains(replace, MaintainerFile, StringComparison.Ordinal);
        return MaintainerFile.Replace(replace, context, StringComparison.Ordinal);
    }

    private static string TopLevelFile() => """
        using Microsoft.EntityFrameworkCore;

        var db = new Ctx();
        string name = "x";
        CancellationToken ct = CancellationToken.None;
        var b = db.Users.ToList().Where(u => u.Name == name).ToList();
        if (db.Users.Count() > 0) Console.WriteLine("any");
        foreach (var u in db.Users.ToList()) { u.Name += "!"; db.SaveChanges(); }
        var f = db.Users.FirstOrDefault();
        var page = db.Users.Skip(10).Take(10).ToList();
        await Task.Delay(1, ct);
        Console.WriteLine(b.Count + page.Count + (f?.Id ?? 0));

        public class User { public int Id { get; set; } public string Name { get; set; } = ""; }
        public class Ctx : DbContext { public DbSet<User> Users { get; set; } = null!; }
        """;

    private static IEnumerable<(string File, int Line, int Column, string Id)> Parse(string output)
    {
        foreach (Match match in Diagnostic.Matches(output))
        {
            yield return (
                match.Groups["file"].Value,
                int.Parse(match.Groups["line"].Value),
                int.Parse(match.Groups["column"].Value),
                match.Groups["id"].Value);
        }
    }
}
