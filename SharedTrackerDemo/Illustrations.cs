using Microsoft.EntityFrameworkCore;

// Compile-checked snippets used in the article. Not executed by Program.cs.
namespace SharedTrackerDemo.Illustrations;

// The inherited generic repository: it compiles, and it adds very little.
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(int id);
    Task<IEnumerable<T>> GetAllAsync();
    void Add(T entity);
    void Update(T entity);
    void Remove(T entity);
}

public record OrderSummary(int Id, long TotalCents);

// What the DbContext already gives you: projections, no-tracking reads, set-based writes.
public static class ShopQueries
{
    public static Task<List<OrderSummary>> RecentOrdersAsync(this ShopContext db, int customerId, CancellationToken ct = default) =>
        db.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.Id)
            .Select(o => new OrderSummary(o.Id, o.TotalCents))
            .Take(20)
            .ToListAsync(ct);

    public static Task<int> RenameCustomerAsync(this ShopContext db, int customerId, string name, CancellationToken ct = default) =>
        db.Customers
            .Where(c => c.Id == customerId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Name, name), ct);
}

// A repository that earns its place: aggregate-specific, intent-revealing, no SaveChanges inside.
public interface ICustomerRepository
{
    Task<Customer?> FindForCheckoutAsync(int customerId, CancellationToken ct = default);
    void Add(Customer customer);
}

public sealed class CustomerRepository(ShopContext db) : ICustomerRepository
{
    public Task<Customer?> FindForCheckoutAsync(int customerId, CancellationToken ct = default) =>
        db.Customers
            .Include(c => c.Orders)
            .SingleOrDefaultAsync(c => c.Id == customerId, ct);

    public void Add(Customer customer) => db.Customers.Add(customer);
}
