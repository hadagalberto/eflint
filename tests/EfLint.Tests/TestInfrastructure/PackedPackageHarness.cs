using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Xunit;

namespace EfLint.Tests.TestInfrastructure;

/// <summary>
/// Compiles a consumer with the analyzer DLLs extracted from the nupkg that was
/// just packed. The compiler is invoked directly (<c>dotnet exec csc.dll</c>) with
/// the real EF Core 8 reference assemblies, exactly like a <c>dotnet build</c>
/// would pass <c>/analyzer:</c> to csc — but without nested MSBuild/NuGet restores
/// that can stall on shared build agents.
/// </summary>
internal static class PackedPackageHarness
{
    private const string AnalyzerEntry = "analyzers/dotnet/cs/EfLint.dll";
    private const string CodeFixesEntry = "analyzers/dotnet/cs/EfLint.CodeFixes.dll";

    public static async Task<string> PackAsync(string workspace, string repositoryRoot)
    {
        var packageDirectory = Path.Combine(workspace, "packages");
        Directory.CreateDirectory(packageDirectory);

        var (exitCode, output) = await DotNetRunner.RunAsync(
            repositoryRoot,
            "pack",
            "src/EfLint.Package/EfLint.Package.csproj",
            "-c",
            "Release",
            "-o",
            packageDirectory,
            "--nologo",
            "-v:m").ConfigureAwait(false);

        Assert.True(exitCode == 0, $"dotnet pack failed:\n{output}");
        Assert.Single(Directory.GetFiles(packageDirectory, "*.nupkg"));
        return packageDirectory;
    }

    public static async Task<string> CompileWithPackedAnalyzerAsync(
        string workspace,
        string name,
        string packagePath,
        IReadOnlyDictionary<string, string> sources)
    {
        var directory = Path.Combine(workspace, name);
        Directory.CreateDirectory(directory);

        var analyzerDirectory = Path.Combine(directory, "analyzers");
        Directory.CreateDirectory(analyzerDirectory);
        using (var archive = ZipFile.OpenRead(packagePath))
        {
            foreach (var entryName in new[] { AnalyzerEntry, CodeFixesEntry })
            {
                var entry = archive.GetEntry(entryName);
                Assert.NotNull(entry);
                entry!.ExtractToFile(Path.Combine(analyzerDirectory, Path.GetFileName(entryName)), overwrite: true);
            }
        }

        var editorConfig = Path.Combine(directory, ".editorconfig");
        await File.WriteAllTextAsync(editorConfig, EditorConfig).ConfigureAwait(false);

        var globalUsings = Path.Combine(directory, "GlobalUsings.g.cs");
        await File.WriteAllTextAsync(globalUsings, GlobalUsings).ConfigureAwait(false);

        var sourcePaths = new List<string> { globalUsings };
        foreach (var source in sources)
        {
            var path = Path.Combine(directory, source.Key);
            await File.WriteAllTextAsync(path, source.Value).ConfigureAwait(false);
            sourcePaths.Add(path);
        }

        var arguments = new List<string>
        {
            "exec",
            FindCsc(),
            "/noconfig",
            "/nostdlib+",
            "/target:exe",
            "/langversion:12",
            "/nullable:enable",
            "/out:" + Path.Combine(directory, "Consumer.dll"),
        };

        arguments.AddRange(FindFrameworkReferences().Select(path => "/reference:" + path));
        arguments.AddRange(FindEfCoreReferences().Select(path => "/reference:" + path));
        arguments.Add("/analyzer:" + Path.Combine(analyzerDirectory, "EfLint.dll"));
        arguments.Add("/analyzer:" + Path.Combine(analyzerDirectory, "EfLint.CodeFixes.dll"));
        arguments.Add("/analyzerconfig:" + editorConfig);
        arguments.AddRange(sourcePaths);

        var (exitCode, output) = await DotNetRunner.RunAsync(directory, arguments.ToArray()).ConfigureAwait(false);
        Assert.True(exitCode == 0, $"csc failed for '{name}':\n{output}");
        Assert.DoesNotContain(": error CS", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AD0001", output, StringComparison.OrdinalIgnoreCase);
        return output;
    }

    public static string CreateWorkspace(string prefix) =>
        Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");

    private const string GlobalUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.Linq;
        global using System.Threading;
        global using System.Threading.Tasks;
        """;

    private const string EditorConfig = """
        root = true

        [*.cs]
        dotnet_diagnostic.EFL1001.severity = warning
        dotnet_diagnostic.EFL1002.severity = warning
        dotnet_diagnostic.EFL1003.severity = warning
        dotnet_diagnostic.EFL1004.severity = warning
        dotnet_diagnostic.EFL1005.severity = warning
        dotnet_diagnostic.EFL1006.severity = warning
        dotnet_diagnostic.EFL1007.severity = warning
        dotnet_diagnostic.EFL1008.severity = warning
        dotnet_diagnostic.EFL1009.severity = warning
        dotnet_diagnostic.EFL1010.severity = warning
        dotnet_diagnostic.EFL2001.severity = warning
        dotnet_diagnostic.EFL2002.severity = warning
        dotnet_diagnostic.EFL2003.severity = warning
        dotnet_diagnostic.EFL2004.severity = warning
        dotnet_diagnostic.EFL2005.severity = warning
        dotnet_diagnostic.EFL2006.severity = warning
        dotnet_diagnostic.EFL2007.severity = warning
        dotnet_diagnostic.EFL2008.severity = warning
        dotnet_diagnostic.EFL3001.severity = warning
        dotnet_diagnostic.EFL3002.severity = warning
        """;

    private static string FindDotNetRoot()
    {
        var runtime = RuntimeEnvironment.GetRuntimeDirectory();
        return Path.GetFullPath(Path.Combine(runtime, "..", "..", ".."));
    }

    private static string FindCsc()
    {
        var sdkRoot = Path.Combine(FindDotNetRoot(), "sdk");
        var sdk = Directory.GetDirectories(sdkRoot)
            .Where(path => File.Exists(Path.Combine(path, "Roslyn", "bincore", "csc.dll")))
            .OrderByDescending(path => Path.GetFileName(path), StringComparer.Ordinal)
            .FirstOrDefault(path => Path.GetFileName(path).StartsWith("8.", StringComparison.Ordinal))
            ?? Directory.GetDirectories(sdkRoot)
                .Where(path => File.Exists(Path.Combine(path, "Roslyn", "bincore", "csc.dll")))
                .OrderByDescending(path => path, StringComparer.Ordinal)
                .First();
        return Path.Combine(sdk, "Roslyn", "bincore", "csc.dll");
    }

    private static IReadOnlyList<string> FindFrameworkReferences()
    {
        var root = Path.Combine(FindDotNetRoot(), "packs", "Microsoft.NETCore.App.Ref");
        var version = Directory.GetDirectories(root)
            .Where(path => Path.GetFileName(path).StartsWith("8.", StringComparison.Ordinal))
            .OrderByDescending(path => path, StringComparer.Ordinal)
            .First();
        return Directory.GetFiles(Path.Combine(version, "ref", "net8.0"), "*.dll");
    }

    private static IReadOnlyList<string> FindEfCoreReferences()
    {
        var nugetRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        string[] packages =
        {
            "microsoft.entityframeworkcore",
            "microsoft.entityframeworkcore.abstractions",
            "microsoft.entityframeworkcore.relational",
            "microsoft.entityframeworkcore.sqlite",
            "microsoft.entityframeworkcore.sqlite.core",
            "microsoft.extensions.dependencyinjection",
            "microsoft.extensions.dependencyinjection.abstractions",
            "microsoft.extensions.logging.abstractions",
            "microsoft.extensions.caching.memory",
            "microsoft.extensions.caching.abstractions",
        };

        var references = new List<string>();
        foreach (var package in packages)
        {
            var root = Path.Combine(nugetRoot, package);
            if (!Directory.Exists(root))
            {
                continue;
            }

            var version = Directory.GetDirectories(root)
                .OrderByDescending(path => path, StringComparer.Ordinal)
                .FirstOrDefault();
            if (version is null)
            {
                continue;
            }

            var lib = Path.Combine(version, "lib", "net8.0");
            if (Directory.Exists(lib))
            {
                references.AddRange(Directory.GetFiles(lib, "*.dll"));
            }
        }

        Assert.True(references.Any(path => path.EndsWith("Microsoft.EntityFrameworkCore.dll", StringComparison.OrdinalIgnoreCase)),
            "EF Core 8 reference assemblies were not found in the NuGet cache.");
        return references;
    }
}
