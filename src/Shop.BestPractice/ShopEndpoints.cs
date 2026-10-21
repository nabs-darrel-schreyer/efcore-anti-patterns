using Microsoft.EntityFrameworkCore;
using Shop.Shared;

namespace Shop.BestPractice;

public static class ShopEndpoints
{
    public static void MapShopEndpoints(this IEndpointRouteBuilder routes)
    {
        var customers = routes.MapGroup("/customers").WithTags("Customers");

        // A scoped context is a good fit here: one handler, one read, no SaveChanges.
        customers.MapGet("/", async (string? search, ShopContext db, CancellationToken ct) =>
        {
            using var activity = ShopActivity.Start("List customers", ShopActivity.BestPractice, "list-customers", "ShopContext");
            var query = db.Customers.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(c => EF.Functions.Like(c.Name, $"%{search}%"));

            return await query
                .OrderBy(c => c.Id)
                .Select(c => new CustomerSummary(c.Id, c.Name, c.Email, c.Orders.Count))
                .ToListAsync(ct);
        })
        .WithName("ListCustomers")
        .WithSummary("No-tracking projection. The search is part of the SQL.");

        customers.MapGet("/{id:int}", async (int id, ShopContext db, CancellationToken ct) =>
        {
            var customer = await db.Customers.AsNoTracking()
                .Where(c => c.Id == id)
                .Select(c => new CustomerSummary(c.Id, c.Name, c.Email, c.Orders.Count))
                .SingleOrDefaultAsync(ct);
            return customer is null ? Results.NotFound() : Results.Ok(customer);
        })
        .WithName("GetCustomer");

        customers.MapPatch("/{id:int}", async (int id, RenameCustomerRequest request, IDbContextFactory<ShopContext> factory, CancellationToken ct) =>
        {
            using var activity = ShopActivity.Start("Rename customer", ShopActivity.BestPractice, "rename-customer", "ShopContext");
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });

            var name = request.Name.Trim();
            await using var db = await factory.CreateDbContextAsync(ct);
            var updated = await db.Customers
                .Where(c => c.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.Name, name), ct);
            if (updated == 0)
                return Results.NotFound();

            return Results.Ok(new RenameCustomerResponse(id, name, WriteLog.Summaries(), WriteLog.Statements()));
        })
        .WithName("RenameCustomer")
        .WithSummary("A single UPDATE via ExecuteUpdate. The customer is not loaded.");

        routes.MapGet("/products", async (ShopContext db, CancellationToken ct) =>
        {
            using var activity = ShopActivity.Start("List products", ShopActivity.BestPractice, "list-products", "ShopContext");
            return await db.Products.AsNoTracking()
                .OrderBy(p => p.Id)
                .Select(p => new ProductSummary(p.Id, p.Name, p.Sku, p.PriceCents))
                .ToListAsync(ct);
        })
        .WithTags("Products")
        .WithName("ListProducts");

        var orders = routes.MapGroup("/orders").WithTags("Orders");
        orders.MapGet("/", async (ShopContext db, CancellationToken ct) =>
        {
            using var activity = ShopActivity.Start("List orders", ShopActivity.BestPractice, "list-orders", "ShopContext");
            return await ProjectOrders(db.Orders.AsNoTracking().OrderByDescending(o => o.Id)).ToListAsync(ct);
        })
        .WithName("ListOrders");

        orders.MapGet("/{id:int}", async (int id, ShopContext db, CancellationToken ct) =>
        {
            var order = await ProjectOrders(db.Orders.AsNoTracking().Where(o => o.Id == id)).SingleOrDefaultAsync(ct);
            return order is null ? Results.NotFound() : Results.Ok(order);
        })
        .WithName("GetOrder");

        orders.MapPost("/", async (PlaceOrderRequest request, PlaceOrderHandler handler, CancellationToken ct) =>
        {
            if (request.Lines is null || request.Lines.Count == 0 || request.Lines.Any(l => l.Quantity < 1))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["lines"] = ["Add at least one line with a quantity of 1 or more."]
                });
            }

            try
            {
                var response = await handler.HandleAsync(request, ct);
                return response is null ? Results.NotFound() : Results.Ok(response);
            }
            catch (InvalidOrderException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["lines"] = [ex.Message] });
            }
        })
        .WithName("PlaceOrder")
        .WithSummary("Each unit of work has its own short-lived context. Only the order is saved.");
    }

    private static IQueryable<OrderSummary> ProjectOrders(IQueryable<Order> orders) =>
        orders.Select(o => new OrderSummary(
            o.Id,
            o.CustomerId,
            o.Customer!.Name,
            o.PlacedAt,
            o.TotalCents,
            o.Lines.Select(l => new OrderLineSummary(l.ProductId, l.Product!.Name, l.Quantity, l.UnitPriceCents)).ToList()));
}
