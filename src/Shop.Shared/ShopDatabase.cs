using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Shop.Shared;

public static class ShopDatabase
{
    public const string ConnectionStringName = "Shop";

    public static string ResolveConnectionString(IHostApplicationBuilder builder, string fileName)
    {
        var configured = builder.Configuration.GetConnectionString(ConnectionStringName);
        if (!string.IsNullOrWhiteSpace(configured))
            return EnsureDirectory(configured);

        var directory = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(directory);
        return $"Data Source={Path.Combine(directory, fileName)}";
    }

    public static void Configure(DbContextOptionsBuilder options, string connectionString, WriteLog writes)
    {
        EnsureDirectory(connectionString);
        options.UseSqlite(connectionString);
        options.AddInterceptors(writes);
    }

    private static string EnsureDirectory(string connectionString)
    {
        var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource;
        if (string.IsNullOrWhiteSpace(dataSource) || dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
            return connectionString;

        var directory = Path.GetDirectoryName(dataSource);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        return connectionString;
    }
}
