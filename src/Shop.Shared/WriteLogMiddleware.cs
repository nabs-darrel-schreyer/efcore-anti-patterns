using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Shop.Shared;

public static class WriteLogMiddleware
{
    public const string HeaderName = "X-Shop-Writes";

    public static void UseShopWriteLog(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var writes = WriteLog.Begin();
            context.Response.OnStarting(() =>
            {
                if (writes.Count > 0)
                    context.Response.Headers[HeaderName] = string.Join(", ", writes.Select(w => w.Summary));
                return Task.CompletedTask;
            });
            await next();
        });
    }
}
