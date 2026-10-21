using Microsoft.EntityFrameworkCore;

namespace SharedTrackerDemo.Scoped;

// The inherited shape: one scoped ShopContext shared by every service in the request.

// Service A: "just formatting" a greeting for the confirmation email.
public sealed class GreetingService(ShopContext db)
{
    public async Task<string> BuildGreetingAsync(int customerId)
    {
        var customer = await db.Customers.SingleAsync(c => c.Id == customerId); // tracked
        customer.Name = customer.Name.Trim().ToUpperInvariant();                // mutates a tracked entity
        return $"Hello {customer.Name}";
    }
}

// Service B: places the order and saves. It never touches Customer.
public sealed class OrderService(ShopContext db)
{
    public async Task PlaceOrderAsync(int customerId, long totalCents)
    {
        db.Orders.Add(new Order { CustomerId = customerId, TotalCents = totalCents });
        await db.SaveChangesAsync(); // saves *everything* this context is tracking
    }
}

public sealed class CheckoutHandler(GreetingService greetings, OrderService orders, ShopContext db)
{
    public int TrackedBeforeSave { get; private set; }

    public async Task<string> HandleAsync(int customerId, long totalCents)
    {
        var greeting = await greetings.BuildGreetingAsync(customerId);
        TrackedBeforeSave = db.ChangeTracker.Entries().Count(e => e.State != EntityState.Unchanged);
        await orders.PlaceOrderAsync(customerId, totalCents);
        return greeting;
    }
}
