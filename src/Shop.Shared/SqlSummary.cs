namespace Shop.Shared;

/// <summary>Turns a command into a short label such as "UPDATE Customers" or "INSERT Orders".</summary>
public static class SqlSummary
{
    public static string Describe(string commandText)
    {
        var parts = new List<string>();
        foreach (var statement in commandText.Split(';'))
        {
            var summary = Summarise(statement);
            if (summary is not null)
                parts.Add(summary);
        }

        return parts.Count == 0 ? "SQL" : string.Join(", ", parts);
    }

    public static string? Summarise(string statement)
    {
        var words = statement.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return null;

        var verb = words[0].ToUpperInvariant();
        switch (verb)
        {
            case "UPDATE":
                return words.Length > 1 ? $"UPDATE {Clean(words[1])}" : null;
            case "INSERT":
            case "DELETE":
            case "REPLACE":
                return words.Length > 2 ? $"{verb} {Clean(words[2])}" : null;
            case "SELECT":
                for (var i = 0; i < words.Count() - 1; i++)
                {
                    if (words[i].Equals("FROM", StringComparison.OrdinalIgnoreCase))
                        return $"SELECT {Clean(words[i + 1])}";
                }

                return "SELECT";
            default:
                return null;
        }
    }

    public static bool IsWrite(string? summary) =>
        summary is not null &&
        (summary.StartsWith("INSERT ", StringComparison.Ordinal) ||
         summary.StartsWith("UPDATE ", StringComparison.Ordinal) ||
         summary.StartsWith("DELETE ", StringComparison.Ordinal) ||
         summary.StartsWith("REPLACE ", StringComparison.Ordinal));

    private static string Clean(string token) => token.Trim('"', '`', '[', ']', ',', '(', ')');
}
