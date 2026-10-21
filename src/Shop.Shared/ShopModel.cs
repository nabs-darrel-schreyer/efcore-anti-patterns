namespace Shop.Shared;

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public List<Order> Orders { get; set; } = [];
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Sku { get; set; } = "";
    public int PriceCents { get; set; }
}

public class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public DateTimeOffset PlacedAt { get; set; }
    public int TotalCents { get; set; }
    public List<OrderLine> Lines { get; set; } = [];
}

public class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int Quantity { get; set; }
    public int UnitPriceCents { get; set; }
}

public sealed record CustomerSummary(int Id, string Name, string Email, int OrderCount);

public sealed record ProductSummary(int Id, string Name, string Sku, int PriceCents);

public sealed record OrderLineSummary(int ProductId, string ProductName, int Quantity, int UnitPriceCents);

public sealed record OrderSummary(
    int Id,
    int CustomerId,
    string CustomerName,
    DateTimeOffset PlacedAt,
    int TotalCents,
    IReadOnlyList<OrderLineSummary> Lines);

public sealed record OrderLineRequest(int ProductId, int Quantity);

public sealed record PlaceOrderRequest(int CustomerId, IReadOnlyList<OrderLineRequest> Lines);

public sealed record PlaceOrderResponse(
    int OrderId,
    string Greeting,
    string CustomerNameBefore,
    string StoredCustomerName,
    bool CustomerNameChanged,
    IReadOnlyList<string> Writes,
    IReadOnlyList<string> Statements);

public sealed record RenameCustomerRequest(string Name);

public sealed record RenameCustomerResponse(int CustomerId, string StoredName, IReadOnlyList<string> Writes, IReadOnlyList<string> Statements);
