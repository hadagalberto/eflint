using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace EfLint.Tests.TestInfrastructure;

/// <summary>
/// Applies a code fix outside the Roslyn testing harness so a test can assert two
/// things the harness does not express directly: (a) that no fix is offered for a
/// source that cannot be fixed safely, and (b) that the fixed document compiles
/// against the real EF Core reference assemblies with zero compiler errors.
/// </summary>
internal static class CodeFixProbe
{
    private static readonly Lazy<Task<ImmutableArray<MetadataReference>>> References = new(
        () => EfLintTestResources.EfCore.ResolveAsync(LanguageNames.CSharp, CancellationToken.None));

    public static async Task<(Document Document, ImmutableArray<Diagnostic> AnalyzerDiagnostics)> AnalyzeAsync(
        string source,
        DiagnosticAnalyzer analyzer)
    {
        var references = await References.Value.ConfigureAwait(false);
        var projectId = ProjectId.CreateNewId("EFL1007FixProbe");
        var documentId = DocumentId.CreateNewId(projectId, "Probe.cs");
        var solution = new AdhocWorkspace().CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "FixProbe",
                "FixProbe",
                LanguageNames.CSharp,
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                metadataReferences: references))
            .AddDocument(documentId, "Probe.cs", SourceText.From(source));

        var document = solution.GetDocument(documentId)!;
        var compilation = (CSharpCompilation)(await document.Project.GetCompilationAsync().ConfigureAwait(false))!;
        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create(analyzer))
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(false);
        return (document, diagnostics);
    }

    public static async Task<ImmutableArray<CodeAction>> GetActionsAsync<TFix>(
        Document document,
        Diagnostic diagnostic)
        where TFix : CodeFixProvider, new()
    {
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None);
        await new TFix().RegisterCodeFixesAsync(context).ConfigureAwait(false);
        return actions.ToImmutableArray();
    }

    public static async Task<Document> ApplyAsync(Document document, CodeAction action)
    {
        var operations = await action.GetOperationsAsync(CancellationToken.None).ConfigureAwait(false);
        var apply = operations.OfType<ApplyChangesOperation>().Single();
        return apply.ChangedSolution.GetDocument(document.Id)!;
    }

    public static async Task<ImmutableArray<Diagnostic>> GetCompilerErrorsAsync(Document document)
    {
        var compilation = await document.Project.GetCompilationAsync().ConfigureAwait(false);
        return compilation!.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
    }

    /// <summary>
    /// Analyzes source, applies the single offered fix and verifies the resulting
    /// document has no compiler errors. Returns the fixed source for assertions.
    /// </summary>
    public static async Task<string> FixAndVerifyAsync<TAnalyzer, TFix>(string source)
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TFix : CodeFixProvider, new()
    {
        var (document, diagnostics) = await AnalyzeAsync(source, new TAnalyzer()).ConfigureAwait(false);
        AssertHasDiagnostic(diagnostics);
        var actions = await GetActionsAsync<TFix>(document, diagnostics[0]).ConfigureAwait(false);
        if (actions.Length != 1)
        {
            throw new InvalidOperationException($"Expected exactly one code action, found {actions.Length}.");
        }

        var fixedDocument = await ApplyAsync(document, actions[0]).ConfigureAwait(false);
        var errors = await GetCompilerErrorsAsync(fixedDocument).ConfigureAwait(false);
        if (errors.Length > 0)
        {
            throw new InvalidOperationException(
                "Fixed code does not compile: " + string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
        }

        var text = await fixedDocument.GetTextAsync().ConfigureAwait(false);
        return text.ToString();
    }

    private static void AssertHasDiagnostic(ImmutableArray<Diagnostic> diagnostics)
    {
        if (diagnostics.IsEmpty)
        {
            throw new InvalidOperationException("The analyzer reported no diagnostic for the probe source.");
        }
    }
}
