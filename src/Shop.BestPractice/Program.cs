using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Shop.BestPractice;
using Shop.Shared;

namespace Shop.BestPractice;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Information);

        builder.Services.AddProblemDetails();
        builder.Services.AddOpenApi();
        builder.Services.AddSingleton<WriteLog>();

        var connectionString = ShopDatabase.ResolveConnectionString(builder, "bestpractice.db");
        builder.Services.AddPooledDbContextFactory<ShopContext>((sp, options) =>
            ShopDatabase.Configure(options, connectionString, sp.GetRequiredService<WriteLog>()));

        // Simple reads take a scoped context from the factory. One handler, one context.
        builder.Services.AddScoped(sp =>
            sp.GetRequiredService<IDbContextFactory<ShopContext>>().CreateDbContext());
        builder.Services.AddScoped<PlaceOrderHandler>();
        builder.Services.AddHostedService<ShopSeedHostedService>();

        var app = builder.Build();
        if (app.Environment.IsDevelopment())
            app.UseDeveloperExceptionPage();
        else
            app.UseExceptionHandler();
        app.UseShopWriteLog();
        app.MapOpenApi();
        app.MapScalarApiReference(options => options.WithTitle("Best-practice API"));
        app.MapShopEndpoints();
        app.MapDefaultEndpoints();
        app.Run();
    }
}
