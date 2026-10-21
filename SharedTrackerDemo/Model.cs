using Microsoft.EntityFrameworkCore;

namespace SharedTrackerDemo;

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Order> Orders { get; } = new();
}

public class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public long TotalCents { get; set; }
}

public class ShopContext(DbContextOptions<ShopContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
}
