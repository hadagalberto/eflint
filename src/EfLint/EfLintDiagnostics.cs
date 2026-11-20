using Microsoft.CodeAnalysis;

namespace EfLint;

/// <summary>Diagnostic descriptors and identifiers owned by EfLint.</summary>
public static class EfLintDiagnostics
{
    public const string RawSqlInjectionId = "EFL3001";
    public const string FlattenedSqlInterpolationId = "EFL3002";

    public const string SyncOverAsyncId = "EFL2001";
    public const string MissingCancellationTokenId = "EFL2002";
    public const string ClientEvaluationId = "EFL2003";
    public const string ContextAcrossThreadsId = "EFL2004";
    public const string SingletonContextId = "EFL2005";
    public const string IncludeWithProjectionId = "EFL2006";
    public const string StringComparisonInQueryId = "EFL2007";
    public const string RedundantUpdateAttachId = "EFL2008";

    public static readonly DiagnosticDescriptor RawSqlInjection = new(
        id: RawSqlInjectionId,
        title: "Raw SQL built from dynamic values",
        messageFormat: "SQL passed to '{0}' contains non-constant values; use '{1}' with a FormattableString instead",
        category: "Security",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "FromSqlRaw, ExecuteSqlRaw and SqlQueryRaw do not parameterize interpolated values. Building SQL this way allows SQL injection.",
        helpLinkUri: Help(RawSqlInjectionId));

    public static readonly DiagnosticDescriptor FlattenedSqlInterpolation = new(
        id: FlattenedSqlInterpolationId,
        title: "Interpolated SQL flattened to a string",
        messageFormat: "Interpolated SQL assigned to a string loses parameter binding; pass it directly to '{1}' instead of '{0}'",
        category: "Security",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An interpolated string stored in a string variable is evaluated before the EF Core raw SQL API sees it, so its values are inlined into the SQL text.",
        helpLinkUri: Help(FlattenedSqlInterpolationId));

    public static readonly DiagnosticDescriptor SyncOverAsync = new(
        id: SyncOverAsyncId,
        title: "Synchronous EF operation inside an async method",
        messageFormat: "Synchronous '{0}' call in an async method; use '{1}' with await instead",
        category: "Correctness",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A synchronous EF Core query or SaveChanges call blocks the calling thread while an asynchronous overload is available.",
        helpLinkUri: Help(SyncOverAsyncId));

    public static readonly DiagnosticDescriptor MissingCancellationToken = new(
        id: MissingCancellationTokenId,
        title: "Async EF operation ignores an available CancellationToken",
        messageFormat: "Async EF operation '{0}' does not pass the available CancellationToken '{1}'",
        category: "Correctness",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Passing an in-scope CancellationToken to asynchronous EF Core operations lets callers cancel long-running database work and avoids wasted work after a shutdown request.",
        helpLinkUri: Help(MissingCancellationTokenId));

    public static readonly DiagnosticDescriptor ClientEvaluation = new(
        id: ClientEvaluationId,
        title: "Non-translatable method inside an EF query",
        messageFormat: "Method '{0}' cannot be translated to SQL; mark it with [DbFunction] or evaluate it outside the query",
        category: "Correctness",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "User methods called inside query lambdas cannot be translated by the EF Core provider. Depending on configuration the query either fails at runtime or silently evaluates on the client after materializing rows.",
        helpLinkUri: Help(ClientEvaluationId));

    public static readonly DiagnosticDescriptor ContextAcrossThreads = new(
        id: ContextAcrossThreadsId,
        title: "DbContext used across threads",
        messageFormat: "DbContext is captured by a '{0}' lambda; DbContext is not thread-safe",
        category: "Correctness",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A single DbContext instance is used concurrently by a Parallel loop or a Task.WhenAll projection. DbContext is not thread-safe; use a separate context per thread.",
        helpLinkUri: Help(ContextAcrossThreadsId));

    public static readonly DiagnosticDescriptor SingletonContext = new(
        id: SingletonContextId,
        title: "DbContext registered as a singleton",
        messageFormat: "DbContext '{0}' is registered with a singleton lifetime; register it as scoped instead",
        category: "Correctness",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "DbContext holds per-request state and is not thread-safe. Registering it as a singleton shares one instance across all requests and leaks tracked entities.",
        helpLinkUri: Help(SingletonContextId));

    public static readonly DiagnosticDescriptor IncludeWithProjection = new(
        id: IncludeWithProjectionId,
        title: "Include ignored after a projection",
        messageFormat: "Include is ignored when the query is projected with Select; remove it or project the navigation explicitly",
        category: "Correctness",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "EF Core drops Include when the query projects to a different shape with Select. The related entities are not loaded, so the Include has no effect.",
        helpLinkUri: Help(IncludeWithProjectionId));

    public static readonly DiagnosticDescriptor StringComparisonInQuery = new(
        id: StringComparisonInQueryId,
        title: "String comparison inside an EF query",
        messageFormat: "String comparison '{0}' may not translate as expected; consider {1}",
        category: "Correctness",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "String.Equals with a StringComparison or ToLower()/ToUpper() comparisons are often non-sargable or unsupported by providers. Prefer a provider-aware collation or ILIKE expression.",
        helpLinkUri: Help(StringComparisonInQueryId));

    public static readonly DiagnosticDescriptor RedundantUpdateAttach = new(
        id: RedundantUpdateAttachId,
        title: "Redundant Update/Attach on a tracked entity",
        messageFormat: "Entity is already tracked by this context; calling '{0}' is redundant and forces a full update",
        category: "Correctness",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Entities returned by a query are tracked by the same context. Calling Update or Attach on them again is unnecessary and marks every property as modified, causing a full database update.",
        helpLinkUri: Help(RedundantUpdateAttachId));

    private static string Help(string id) =>
        $"https://github.com/hadagalberto/eflint/blob/main/docs/rules/{id}.md";
}
