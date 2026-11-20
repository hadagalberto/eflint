using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using EfLint.Tests.TestInfrastructure;
using Xunit;

namespace EfLint.Tests.Integration;

public class PackageTests
{
    [Fact]
    public async Task Pack_ProducesAnalyzerPackageWithExpectedLayout()
    {
        var repositoryRoot = DotNetRunner.FindRepositoryRoot();
        var outputDirectory = Path.Combine(repositoryRoot, "artifacts", "packtest");
        Directory.CreateDirectory(outputDirectory);

        foreach (var stalePackage in Directory.GetFiles(outputDirectory, "*.nupkg"))
        {
            File.Delete(stalePackage);
        }

        var (exitCode, output) = await DotNetRunner.RunAsync(
            repositoryRoot,
            "pack",
            "src/EfLint.Package/EfLint.Package.csproj",
            "-c",
            "Release",
            "-o",
            outputDirectory,
            "--nologo",
            "-v:m");

        Assert.True(exitCode == 0, $"dotnet pack failed:\n{output}");

        var packagePath = Directory.GetFiles(outputDirectory, "*.nupkg").Single();
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("analyzers/dotnet/cs/EfLint.dll", entries);
        Assert.Contains("analyzers/dotnet/cs/EfLint.CodeFixes.dll", entries);
        Assert.Contains("build/EfLint.props", entries);
        Assert.Contains("README.md", entries);
        Assert.Contains("EfLint.nuspec", entries);

        await AssertPackageCanBeConsumedAsync(repositoryRoot, outputDirectory);
    }

    private static async Task AssertPackageCanBeConsumedAsync(string repositoryRoot, string packageDirectory)
    {
        var consumerDirectory = Path.Combine(repositoryRoot, "artifacts", "consumetest");
        if (Directory.Exists(consumerDirectory))
        {
            Directory.Delete(consumerDirectory, recursive: true);
        }

        Directory.CreateDirectory(consumerDirectory);

        await File.WriteAllTextAsync(
            Path.Combine(consumerDirectory, "NuGet.config"),
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="eflint-local" value="{packageDirectory}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        await File.WriteAllTextAsync(
            Path.Combine(consumerDirectory, "Consumer.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <Nullable>enable</Nullable>
                <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
                <MSBuildTreatWarningsAsErrors>false</MSBuildTreatWarningsAsErrors>
                <WarningsNotAsErrors>$(WarningsNotAsErrors);EFL3001;EF1002</WarningsNotAsErrors>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="EfLint" Version="0.1.0" />
                <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="8.0.11" />
              </ItemGroup>
            </Project>
            """);

        await File.WriteAllTextAsync(
            Path.Combine(consumerDirectory, "Sample.cs"),
            """
            using Microsoft.EntityFrameworkCore;

            public class Post { public int Id { get; set; } }

            public class BlogContext : DbContext
            {
                public DbSet<Post> Posts => Set<Post>();
            }

            public static class Sample
            {
                public static void Query(BlogContext db, string search)
                {
                    db.Posts.FromSqlRaw($"SELECT * FROM Posts WHERE Name = {search}");
                }
            }
            """);

        var (exitCode, output) = await DotNetRunner.RunAsync(
            consumerDirectory,
            "build",
            "Consumer.csproj",
            "--nologo",
            "-v:m");

        Assert.True(exitCode == 0, $"Consumer build failed:\n{output}");
        Assert.Contains("EFL3001", output);
    }
}
