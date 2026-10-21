using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Shop.Shared;

public readonly record struct CapturedSql(string Summary, string Sql);

/// <summary>
/// Records INSERT, UPDATE and DELETE statements for the current request.
/// The list lives in AsyncLocal so a single shared interceptor instance is safe.
/// </summary>
public sealed class WriteLog : DbCommandInterceptor
{
    private static readonly AsyncLocal<List<CapturedSql>?> Current = new();

    public static List<CapturedSql> Begin()
    {
        var writes = new List<CapturedSql>();
        Current.Value = writes;
        return writes;
    }

    public static IReadOnlyList<CapturedSql> Snapshot() => Current.Value ?? [];

    public static IReadOnlyList<string> Summaries() => Snapshot().Select(w => w.Summary).ToList();

    public static IReadOnlyList<string> Statements() => Snapshot().Select(w => w.Sql).ToList();

    private static void Record(DbCommand command)
    {
        var bucket = Current.Value;
        if (bucket is null)
            return;

        foreach (var statement in command.CommandText.Split(';'))
        {
            var summary = SqlSummary.Summarise(statement);
            if (!SqlSummary.IsWrite(summary) || summary is null)
                continue;

            bucket.Add(new CapturedSql(summary, statement.Trim()));
        }
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Record(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Record(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }
}
