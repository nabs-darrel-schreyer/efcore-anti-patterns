using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Shop.Shared;

public static class ShopSeeder
{
    public const string ArohaOriginalName = "  Aroha Ngata ";
    public const string ArohaEmail = "aroha.ngata@example.com";

    public static async Task SeedAsync(ShopContext db, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);
        if (await db.Customers.AnyAsync(ct))
            return;

        var aroha = new Customer { Name = ArohaOriginalName, Email = ArohaEmail };
        var nikau = new Customer { Name = "Nikau Rangi", Email = "nikau.rangi@example.com" };
        var honey = new Product { Name = "Mānuka honey 250g", Sku = "HNY-250", PriceCents = 1850 };
        var biscuits = new Product { Name = "Wattleseed biscuits", Sku = "WSB-180", PriceCents = 640 };

        db.AddRange(aroha, nikau, honey, biscuits);
        await db.SaveChangesAsync(ct);

        db.Orders.Add(new Order
        {
            CustomerId = nikau.Id,
            PlacedAt = new DateTimeOffset(2026, 3, 2, 9, 30, 0, TimeSpan.FromHours(13)),
            TotalCents = biscuits.PriceCents,
            Lines =
            [
                new OrderLine
                {
                    ProductId = biscuits.Id,
                    Quantity = 1,
                    UnitPriceCents = biscuits.PriceCents
                }
            ]
        });
        await db.SaveChangesAsync(ct);
    }
}

public sealed class ShopSeedHostedService(IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopContext>();
        await ShopSeeder.SeedAsync(db, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
