using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Shop.AntiPatterns;
using Shop.Shared;

namespace Shop.AntiPatterns;

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

        var connectionString = ShopDatabase.ResolveConnectionString(builder, "antipatterns.db");
        builder.Services.AddDbContext<ShopContext>((sp, options) =>
            ShopDatabase.Configure(options, connectionString, sp.GetRequiredService<WriteLog>()));

        builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
        builder.Services.AddScoped<GreetingService>();
        builder.Services.AddScoped<OrderService>();
        builder.Services.AddHostedService<ShopSeedHostedService>();

        var app = builder.Build();
        if (app.Environment.IsDevelopment())
            app.UseDeveloperExceptionPage();
        else
            app.UseExceptionHandler();
        app.UseShopWriteLog();
        app.MapOpenApi();
        app.MapScalarApiReference(options => options.WithTitle("Anti-patterns API"));
        app.MapShopEndpoints();
        app.MapDefaultEndpoints();
        app.Run();
    }
}
