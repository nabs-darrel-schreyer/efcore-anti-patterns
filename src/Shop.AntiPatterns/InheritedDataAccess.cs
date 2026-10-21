using Microsoft.EntityFrameworkCore;
using Shop.Shared;

namespace Shop.AntiPatterns;

// The inherited generic repository: it compiles, and it adds very little.
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<List<T>> GetAllAsync(CancellationToken ct = default);
    void Add(T entity);
    void Update(T entity);
    void Remove(T entity);
}

public sealed class Repository<T>(ShopContext db) : IRepository<T> where T : class
{
    public Task<T?> GetByIdAsync(int id, CancellationToken ct = default) =>
        db.Set<T>().FindAsync([id], ct).AsTask();

    public async Task<List<T>> GetAllAsync(CancellationToken ct = default)
    {
        // GetAll has nowhere to put Include, AsNoTracking, or a projection,
        // so the inherited fix is to load the largest graph anyone might need.
        IQueryable<T> query = db.Set<T>();
        if (typeof(T) == typeof(Customer))
        {
            query = (IQueryable<T>)(object)db.Customers
                .Include(c => c.Orders)
                .ThenInclude(o => o.Lines)
                .ThenInclude(l => l.Product);
        }
        else if (typeof(T) == typeof(Order))
        {
            query = (IQueryable<T>)(object)db.Orders
                .Include(o => o.Customer)
                .Include(o => o.Lines)
                .ThenInclude(l => l.Product);
        }
        else if (typeof(T) == typeof(Product))
        {
            query = db.Set<T>();
        }

        return await query.ToListAsync(ct);
    }

    public void Add(T entity) => db.Set<T>().Add(entity);
    public void Update(T entity) => db.Set<T>().Update(entity);
    public void Remove(T entity) => db.Set<T>().Remove(entity);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public sealed class UnitOfWork(ShopContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

// One scoped ShopContext, shared by every service in the request.
public sealed class GreetingService(ShopContext db)
{
    public async Task<string> BuildGreetingAsync(int customerId, CancellationToken ct)
    {
        using var activity = ShopActivity.Start("Format greeting", ShopActivity.AntiPatterns, "place-order", "GreetingService");
        var customer = await db.Customers.SingleAsync(c => c.Id == customerId, ct);
        customer.Name = customer.Name.Trim().ToUpperInvariant();
        return $"Hello {customer.Name}";
    }
}

public sealed class OrderService(IRepository<Product> products, IRepository<Order> orders, IUnitOfWork unitOfWork)
{
    public async Task<int> PlaceAsync(PlaceOrderRequest request, CancellationToken ct)
    {
        using var activity = ShopActivity.Start("Save order", ShopActivity.AntiPatterns, "place-order", "OrderService");
        var catalogue = await products.GetAllAsync(ct);
        var lines = new List<OrderLine>();
        foreach (var line in request.Lines)
        {
            var product = catalogue.Single(p => p.Id == line.ProductId);
            lines.Add(new OrderLine
            {
                ProductId = product.Id,
                Quantity = line.Quantity,
                UnitPriceCents = product.PriceCents
            });
        }

        var order = new Order
        {
            CustomerId = request.CustomerId,
            PlacedAt = DateTimeOffset.UtcNow,
            TotalCents = lines.Sum(l => l.UnitPriceCents * l.Quantity),
            Lines = lines
        };
        orders.Add(order);
        await unitOfWork.SaveChangesAsync(ct);
        return order.Id;
    }
}
