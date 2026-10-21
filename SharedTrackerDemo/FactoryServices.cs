using Microsoft.EntityFrameworkCore;

namespace SharedTrackerDemo.Factory;

// The fix: each unit of work creates, uses and disposes its own short-lived context.

public sealed class GreetingService(IDbContextFactory<ShopContext> factory)
{
    public async Task<string> BuildGreetingAsync(int customerId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var name = await db.Customers
            .AsNoTracking()
            .Where(c => c.Id == customerId)
            .Select(c => c.Name)
            .SingleAsync();
        return $"Hello {name.Trim().ToUpperInvariant()}";
    }
}

public sealed class OrderService(IDbContextFactory<ShopContext> factory)
{
    public async Task PlaceOrderAsync(int customerId, long totalCents)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.Orders.Add(new Order { CustomerId = customerId, TotalCents = totalCents });
        await db.SaveChangesAsync(); // this context only knows about the new Order
    }
}

// Proof that isolation alone fixes the leak: the *original* mutating code, but in its own context.
public sealed class LegacyGreetingInOwnContext(IDbContextFactory<ShopContext> factory)
{
    public async Task<string> BuildGreetingAsync(int customerId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var customer = await db.Customers.SingleAsync(c => c.Id == customerId);
        customer.Name = customer.Name.Trim().ToUpperInvariant(); // tracked, but discarded on dispose
        return $"Hello {customer.Name}";
    }
}
