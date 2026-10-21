using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedTrackerDemo;

// Shared-cache in-memory SQLite: the database lives as long as `keepAlive` stays open.
const string cs = "Data Source=shared-tracker-demo;Mode=Memory;Cache=Shared";
await using var keepAlive = new SqliteConnection(cs);
await keepAlive.OpenAsync();

var failures = 0;
void Check(bool ok, string what)
{
    Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
    if (!ok) failures++;
}

// Seeding and verification use their own plain context, outside the DI setups under test.
ShopContext AdminContext() => new(new DbContextOptionsBuilder<ShopContext>().UseSqlite(cs).Options);

async Task<int> SeedAsync()
{
    await using var db = AdminContext();
    await db.Database.EnsureDeletedAsync();
    await db.Database.EnsureCreatedAsync();
    var customer = new Customer { Name = "  Aroha Ngata " };
    db.Customers.Add(customer);
    await db.SaveChangesAsync();
    return customer.Id;
}

async Task<string> ReadNameAsync(int id)
{
    await using var db = AdminContext();
    return await db.Customers.AsNoTracking().Where(c => c.Id == id).Select(c => c.Name).SingleAsync();
}

// ---------------------------------------------------------------- 1. Scoped, shared context
{
    Console.WriteLine("1) One scoped DbContext shared by GreetingService + OrderService");
    var log = new WriteLog();
    var services = new ServiceCollection();
    services.AddDbContext<ShopContext>(o => o.UseSqlite(cs).AddInterceptors(log));
    services.AddScoped<SharedTrackerDemo.Scoped.GreetingService>();
    services.AddScoped<SharedTrackerDemo.Scoped.OrderService>();
    services.AddScoped<SharedTrackerDemo.Scoped.CheckoutHandler>();
    await using var sp = services.BuildServiceProvider(validateScopes: true);

    var id = await SeedAsync();
    log.Writes.Clear();

    await using (var scope = sp.CreateAsyncScope()) // one "request"
    {
        var handler = scope.ServiceProvider.GetRequiredService<SharedTrackerDemo.Scoped.CheckoutHandler>();
        Console.WriteLine($"  greeting: {await handler.HandleAsync(id, 4_950)}");
        Console.WriteLine($"  entities pending before OrderService saved: {handler.TrackedBeforeSave}");
    }

    var name = await ReadNameAsync(id);
    Console.WriteLine($"  writes sent: {string.Join(", ", log.Writes)}");
    Console.WriteLine($"  customer name in database: '{name}'");
    Check(log.Writes.Contains("UPDATE Customers"), "surprise reproduced: OrderService's SaveChanges also UPDATEd Customers");
    Check(name == "AROHA NGATA", "GreetingService's 'formatting' was persisted");
}

// ---------------------------------------------------------------- 2/3. Short-lived contexts from a factory
foreach (var pooled in new[] { false, true })
{
    var label = pooled ? "AddPooledDbContextFactory" : "AddDbContextFactory";
    Console.WriteLine();
    Console.WriteLine($"{(pooled ? 3 : 2)}) {label}: a short-lived context per unit of work");
    var log = new WriteLog();
    var services = new ServiceCollection();
    if (pooled) services.AddPooledDbContextFactory<ShopContext>(o => o.UseSqlite(cs).AddInterceptors(log));
    else services.AddDbContextFactory<ShopContext>(o => o.UseSqlite(cs).AddInterceptors(log));
    services.AddTransient<SharedTrackerDemo.Factory.GreetingService>();
    services.AddTransient<SharedTrackerDemo.Factory.LegacyGreetingInOwnContext>();
    services.AddTransient<SharedTrackerDemo.Factory.OrderService>();
    await using var sp = services.BuildServiceProvider(validateScopes: true);

    var id = await SeedAsync();
    log.Writes.Clear();

    var greetings = sp.GetRequiredService<SharedTrackerDemo.Factory.GreetingService>();
    var legacy = sp.GetRequiredService<SharedTrackerDemo.Factory.LegacyGreetingInOwnContext>();
    var orders = sp.GetRequiredService<SharedTrackerDemo.Factory.OrderService>();

    Console.WriteLine($"  greeting (projection):        {await greetings.BuildGreetingAsync(id)}");
    Console.WriteLine($"  greeting (legacy, own ctx):   {await legacy.BuildGreetingAsync(id)}");
    await orders.PlaceOrderAsync(id, 4_950);

    var name = await ReadNameAsync(id);
    Console.WriteLine($"  writes sent: {string.Join(", ", log.Writes)}");
    Console.WriteLine($"  customer name in database: '{name}'");
    Check(log.Writes.SequenceEqual(new[] { "INSERT Orders" }), "only the Order was written");
    Check(name == "  Aroha Ngata ", "customer row untouched");
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) FAILED.");
return failures == 0 ? 0 : 1;
