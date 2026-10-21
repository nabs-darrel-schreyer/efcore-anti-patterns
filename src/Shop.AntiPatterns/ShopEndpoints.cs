using Microsoft.EntityFrameworkCore;
using Shop.Shared;

namespace Shop.AntiPatterns;

public static class ShopEndpoints
{
    public static void MapShopEndpoints(this IEndpointRouteBuilder routes)
    {
        var customers = routes.MapGroup("/customers").WithTags("Customers");
        customers.MapGet("/", async (string? search, IRepository<Customer> repository, CancellationToken ct) =>
        {
            using var activity = ShopActivity.Start("List customers", ShopActivity.AntiPatterns, "list-customers", "IRepository<Customer>");
            var all = await repository.GetAllAsync(ct);
            IEnumerable<Customer> filtered = all;
            if (!string.IsNullOrWhiteSpace(search))
            {
                filtered = all.Where(c => c.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            return filtered
                .OrderBy(c => c.Id)
                .Select(c => new CustomerSummary(c.Id, c.Name, c.Email, c.Orders.Count))
                .ToList();
        })
        .WithName("ListCustomers")
        .WithSummary("Loads every customer graph, then filters in memory.");

        customers.MapGet("/{id:int}", async (int id, IRepository<Customer> repository, CancellationToken ct) =>
        {
            var all = await repository.GetAllAsync(ct);
            var customer = all.SingleOrDefault(c => c.Id == id);
            return customer is null
                ? Results.NotFound()
                : Results.Ok(new CustomerSummary(customer.Id, customer.Name, customer.Email, customer.Orders.Count));
        })
        .WithName("GetCustomer");

        customers.MapPatch("/{id:int}", async (int id, RenameCustomerRequest request, IRepository<Customer> repository, IUnitOfWork unitOfWork, CancellationToken ct) =>
        {
            using var activity = ShopActivity.Start("Rename customer", ShopActivity.AntiPatterns, "rename-customer", "IRepository<Customer>");
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });

            var customer = await repository.GetByIdAsync(id, ct);
            if (customer is null)
                return Results.NotFound();

            customer.Name = request.Name.Trim();
            repository.Update(customer);
            await unitOfWork.SaveChangesAsync(ct);
            return Results.Ok(new RenameCustomerResponse(customer.Id, customer.Name, WriteLog.Summaries(), WriteLog.Statements()));
        })
        .WithName("RenameCustomer");

        routes.MapGet("/products", async (IRepository<Product> repository, CancellationToken ct) =>
        {
            using var activity = ShopActivity.Start("List products", ShopActivity.AntiPatterns, "list-products", "IRepository<Product>");
            var all = await repository.GetAllAsync(ct);
            return all
                .OrderBy(p => p.Id)
                .Select(p => new ProductSummary(p.Id, p.Name, p.Sku, p.PriceCents))
                .ToList();
        })
        .WithTags("Products")
        .WithName("ListProducts");

        var orders = routes.MapGroup("/orders").WithTags("Orders");
        orders.MapGet("/", async (IRepository<Order> repository, CancellationToken ct) =>
        {
            using var activity = ShopActivity.Start("List orders", ShopActivity.AntiPatterns, "list-orders", "IRepository<Order>");
            var all = await repository.GetAllAsync(ct);
            return all
                .OrderByDescending(o => o.Id)
                .Select(ToSummary)
                .ToList();
        })
        .WithName("ListOrders");

        orders.MapGet("/{id:int}", async (int id, IRepository<Order> repository, CancellationToken ct) =>
        {
            var all = await repository.GetAllAsync(ct);
            var order = all.SingleOrDefault(o => o.Id == id);
            return order is null ? Results.NotFound() : Results.Ok(ToSummary(order));
        })
        .WithName("GetOrder");

        orders.MapPost("/", async (PlaceOrderRequest request, ShopContext db, GreetingService greetings, OrderService orderService, CancellationToken ct) =>
        {
            using var activity = ShopActivity.Start("Place order", ShopActivity.AntiPatterns, "place-order", "Checkout");
            var problem = await ValidateAsync(request, db, ct);
            if (problem is not null)
                return problem;

            var before = await db.Customers.AsNoTracking()
                .Where(c => c.Id == request.CustomerId)
                .Select(c => c.Name)
                .SingleAsync(ct);

            var greeting = await greetings.BuildGreetingAsync(request.CustomerId, ct);
            var orderId = await orderService.PlaceAsync(request, ct);
            var stored = await db.Customers.AsNoTracking()
                .Where(c => c.Id == request.CustomerId)
                .Select(c => c.Name)
                .SingleAsync(ct);

            return Results.Ok(new PlaceOrderResponse(
                orderId,
                greeting,
                before,
                stored,
                stored != before,
                WriteLog.Summaries(),
                WriteLog.Statements()));
        })
        .WithName("PlaceOrder")
        .WithSummary("Places an order. The shared context also saves the greeting service's name formatting.");
    }

    private static OrderSummary ToSummary(Order order) => new(
        order.Id,
        order.CustomerId,
        order.Customer?.Name ?? "",
        order.PlacedAt,
        order.TotalCents,
        order.Lines.Select(l => new OrderLineSummary(l.ProductId, l.Product?.Name ?? "", l.Quantity, l.UnitPriceCents)).ToList());

    private static async Task<IResult?> ValidateAsync(PlaceOrderRequest request, ShopContext db, CancellationToken ct)
    {
        if (request.Lines is null || request.Lines.Count == 0 || request.Lines.Any(l => l.Quantity < 1))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["lines"] = ["Add at least one line with a quantity of 1 or more."]
            });
        }

        var customerExists = await db.Customers.AsNoTracking().AnyAsync(c => c.Id == request.CustomerId, ct);
        if (!customerExists)
            return Results.NotFound();

        var requested = request.Lines.Select(l => l.ProductId).ToHashSet();
        var known = await db.Products.AsNoTracking().Where(p => requested.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct);
        if (known.Count != requested.Count)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["lines"] = ["One or more products do not exist."]
            });
        }

        return null;
    }
}
