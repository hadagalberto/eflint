using System.Collections.Generic;

namespace EfLint;

/// <summary>Mapping from EF Core raw SQL methods to their parameterized counterparts.</summary>
public static class RawSqlMethodNames
{
    private static readonly IReadOnlyDictionary<string, string> SafeNames = new Dictionary<string, string>
    {
        ["FromSqlRaw"] = "FromSql",
        ["ExecuteSqlRaw"] = "ExecuteSql",
        ["ExecuteSqlRawAsync"] = "ExecuteSqlAsync",
        ["SqlQueryRaw"] = "SqlQuery",
    };

    public static bool TryGetSafeName(string rawName, out string? safeName)
    {
        return SafeNames.TryGetValue(rawName, out safeName);
    }
}
