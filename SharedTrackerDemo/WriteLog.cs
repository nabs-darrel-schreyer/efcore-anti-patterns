using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SharedTrackerDemo;

/// <summary>Records every INSERT/UPDATE/DELETE that EF Core sends, e.g. "UPDATE Customers".</summary>
public sealed class WriteLog : DbCommandInterceptor
{
    public List<string> Writes { get; } = new();

    private void Record(DbCommand command)
    {
        foreach (var statement in command.CommandText.Split(';'))
        {
            var words = statement.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2) continue;
            var verb = words[0].ToUpperInvariant();
            var table = verb switch
            {
                "INSERT" or "DELETE" => words.Length > 2 ? words[2] : "?", // INSERT INTO "X" / DELETE FROM "X"
                "UPDATE" => words[1],
                _ => null
            };
            if (table is not null) Writes.Add($"{verb} {table.Trim('"')}");
        }
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand c, CommandEventData e, InterceptionResult<DbDataReader> r)
    { Record(c); return r; }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c, CommandEventData e, InterceptionResult<DbDataReader> r, CancellationToken ct = default)
    { Record(c); return ValueTask.FromResult(r); }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand c, CommandEventData e, InterceptionResult<int> r)
    { Record(c); return r; }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand c, CommandEventData e, InterceptionResult<int> r, CancellationToken ct = default)
    { Record(c); return ValueTask.FromResult(r); }
}
