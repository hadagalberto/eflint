using Microsoft.CodeAnalysis;

namespace EfLint;

/// <summary>Diagnostic descriptors and identifiers owned by EfLint.</summary>
public static class EfLintDiagnostics
{
    public const string RawSqlInjectionId = "EFL3001";
    public const string FlattenedSqlInterpolationId = "EFL3002";

    public const string EarlyMaterializationId = "EFL1001";
    public const string CountComparisonId = "EFL1002";
    public const string MissingAsNoTrackingId = "EFL1003";
    public const string NPlusOneId = "EFL1004";
    public const string FirstWithoutOrderById = "EFL1005";
    public const string PaginationWithoutOrderById = "EFL1006";
    public const string SaveChangesInLoopId = "EFL1007";
    public const string LoadToDeleteUpdateId = "EFL1008";
    public const string LargeContainsId = "EFL1009";
    public const string MultipleIncludesSplitQueryId = "EFL1010";

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

    public static readonly DiagnosticDescriptor EarlyMaterialization = new(
        id: EarlyMaterializationId,
        title: "Query materialized before filtering or projection",
        messageFormat: "'{0}' materializes the EF query before '{1}'; move the materialization to the end of the chain",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Calling ToList, ToArray or AsEnumerable in the middle of a query loads every row into memory before later LINQ operators run. Keep the query as IQueryable until the final materialization so the database does the filtering and projection.",
        helpLinkUri: Help(EarlyMaterializationId));

    public static readonly DiagnosticDescriptor CountComparison = new(
        id: CountComparisonId,
        title: "Count comparison that can be expressed with Any",
        messageFormat: "'{0}' compared with zero/one is better expressed as 'Any()'",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Count() issues a COUNT(*) query and transfers a scalar to the client. When the result is only compared against zero or one, Any() generates a cheaper EXISTS query.",
        helpLinkUri: Help(CountComparisonId));

    public static readonly DiagnosticDescriptor MissingAsNoTracking = new(
        id: MissingAsNoTrackingId,
        title: "Read-only query without AsNoTracking",
        messageFormat: "Query results are only read in this method; consider 'AsNoTracking()' to avoid change-tracking overhead",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "When queried entities are never modified and no SaveChanges follows in the same method, EF Core change tracking is pure overhead. AsNoTracking avoids attaching the entities to the context.",
        helpLinkUri: Help(MissingAsNoTrackingId));

    public static readonly DiagnosticDescriptor NPlusOne = new(
        id: NPlusOneId,
        title: "Possible N+1 query pattern",
        messageFormat: "{0} may issue one query per item; use Include or a single batched query instead",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Accessing an unincluded navigation or issuing a query that depends on the current item inside a loop causes one database round trip per iteration. Eager-load the navigation or rewrite as a single join/batched query.",
        helpLinkUri: Help(NPlusOneId));

    public static readonly DiagnosticDescriptor FirstWithoutOrderBy = new(
        id: FirstWithoutOrderById,
        title: "First/FirstOrDefault without OrderBy",
        messageFormat: "'{0}' has no OrderBy; the selected row is non-deterministic",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Without an explicit OrderBy the database may return any matching row, and the choice can change between calls or query plans. Add a deterministic ordering before First/FirstOrDefault.",
        helpLinkUri: Help(FirstWithoutOrderById));

    public static readonly DiagnosticDescriptor PaginationWithoutOrderBy = new(
        id: PaginationWithoutOrderById,
        title: "Skip/Take without OrderBy",
        messageFormat: "'{0}' is used without OrderBy; paginated results are non-deterministic",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Skip and Take require a deterministic ordering. Without OrderBy the server may return rows in a different order on each request, causing duplicates or missing items across pages.",
        helpLinkUri: Help(PaginationWithoutOrderById));

    public static readonly DiagnosticDescriptor SaveChangesInLoop = new(
        id: SaveChangesInLoopId,
        title: "SaveChanges called inside a loop",
        messageFormat: "'{0}' is called inside a loop; batch the writes and save once after the loop",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Each SaveChanges opens a transaction and a database round trip. Accumulate changes in the loop and call SaveChanges once afterwards, unless a generated key is needed inside the loop.",
        helpLinkUri: Help(SaveChangesInLoopId));

    public static readonly DiagnosticDescriptor LoadToDeleteUpdate = new(
        id: LoadToDeleteUpdateId,
        title: "Entity loaded only to be deleted or updated",
        messageFormat: "Entity is loaded just to '{0}'; consider ExecuteDelete/ExecuteUpdate to avoid the extra round trip",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Loading an entity, modifying or removing it and saving issues a SELECT followed by a command. ExecuteDelete and ExecuteUpdate (EF Core 7+) express the operation in a single statement.",
        helpLinkUri: Help(LoadToDeleteUpdateId));

    public static readonly DiagnosticDescriptor LargeContains = new(
        id: LargeContainsId,
        title: "Large in-memory collection used with Contains in a query",
        messageFormat: "'Contains' uses an in-memory collection of {0} values inside a query; this can produce large SQL or fail on some providers",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Translating a large Contains list creates a large parameter list or inline SQL. Consider a temporary table, a TVP, a join against a staging table, or batching the values.",
        helpLinkUri: Help(LargeContainsId));

    public static readonly DiagnosticDescriptor MultipleIncludesSplitQuery = new(
        id: MultipleIncludesSplitQueryId,
        title: "Multiple collection includes without AsSplitQuery",
        messageFormat: "Multiple collection Includes can cause a cartesian explosion; consider 'AsSplitQuery()'",
        category: "Performance",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Combining several collection navigations in a single SQL query multiplies rows (cartesian explosion). AsSplitQuery executes one query per include and recombines the results in memory.",
        helpLinkUri: Help(MultipleIncludesSplitQueryId));

    private static string Help(string id) =>
        $"https://github.com/hadagalberto/eflint/blob/main/docs/rules/{id}.md";
}
