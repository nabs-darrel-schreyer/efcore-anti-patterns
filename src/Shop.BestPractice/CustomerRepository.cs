using Microsoft.EntityFrameworkCore;
using Shop.Shared;

namespace Shop.BestPractice;

// A repository that earns its place: one aggregate, one intent-revealing query, no SaveChanges.
// The caller creates the context and decides when the unit of work ends.
public interface ICustomerRepository
{
    Task<Customer?> FindForCheckoutAsync(int customerId, CancellationToken ct = default);
}

public sealed class CustomerRepository(ShopContext db) : ICustomerRepository
{
    public Task<Customer?> FindForCheckoutAsync(int customerId, CancellationToken ct = default) =>
        db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == customerId, ct);
}

public sealed class PlaceOrderHandler(IDbContextFactory<ShopContext> factory)
{
    public async Task<PlaceOrderResponse?> HandleAsync(PlaceOrderRequest request, CancellationToken ct)
    {
        using var activity = ShopActivity.Start("Place order", ShopActivity.BestPractice, "place-order", "PlaceOrderHandler");

        string before;
        string greeting;
        await using (var read = await factory.CreateDbContextAsync(ct))
        {
            using var greetingActivity = ShopActivity.Start("Format greeting", ShopActivity.BestPractice, "place-order", "GreetingService");
            var name = await read.Customers.AsNoTracking()
                .Where(c => c.Id == request.CustomerId)
                .Select(c => c.Name)
                .SingleOrDefaultAsync(ct);
            if (name is null)
                return null;

            before = name;
            greeting = $"Hello {name.Trim().ToUpperInvariant()}";
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        using var saveActivity = ShopActivity.Start("Save order", ShopActivity.BestPractice, "place-order", "CustomerRepository");
        var customers = new CustomerRepository(db);
        var customer = await customers.FindForCheckoutAsync(request.CustomerId, ct);
        if (customer is null)
            return null;

        var productIds = request.Lines.Select(l => l.ProductId).ToHashSet();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToListAsync(ct);
        if (products.Count != productIds.Count)
            throw new InvalidOrderException("One or more products do not exist.");

        var lines = request.Lines.Select(line =>
        {
            var product = products.Single(p => p.Id == line.ProductId);
            return new OrderLine
            {
                ProductId = product.Id,
                Quantity = line.Quantity,
                UnitPriceCents = product.PriceCents
            };
        }).ToList();

        var order = new Order
        {
            CustomerId = customer.Id,
            PlacedAt = DateTimeOffset.UtcNow,
            TotalCents = lines.Sum(l => l.UnitPriceCents * l.Quantity),
            Lines = lines
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        var stored = await db.Customers.AsNoTracking()
            .Where(c => c.Id == customer.Id)
            .Select(c => c.Name)
            .SingleAsync(ct);

        return new PlaceOrderResponse(
            order.Id,
            greeting,
            before,
            stored,
            stored != before,
            WriteLog.Summaries(),
            WriteLog.Statements());
    }
}

public sealed class InvalidOrderException(string message) : Exception(message);
