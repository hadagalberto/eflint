using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace EfLint.Tests.TestInfrastructure;

/// <summary>
/// Compiles a source string against the same real EF Core 8 reference assemblies
/// the analyzer tests use. Used by the review-finding regression tests to prove
/// that a fixed document has zero compiler errors (never an unverified snapshot).
/// </summary>
internal static class FixedCodeCompiler
{
    private static readonly Lazy<Task<ImmutableArray<MetadataReference>>> References =
        new(() => EfLintTestResources.EfCore.ResolveAsync(LanguageNames.CSharp, CancellationToken.None));

    public static async Task<ImmutableArray<Diagnostic>> GetErrorsAsync(string source)
    {
        var references = await References.Value.ConfigureAwait(false);
        var compilation = CSharpCompilation.Create(
            "EfLintFixedCodeVerification",
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
    }
}
