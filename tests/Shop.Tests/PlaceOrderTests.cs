using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Shop.Shared;

namespace Shop.Tests;

public class AntiPatternsPlaceOrderTests(ShopApiFactory<Shop.AntiPatterns.Program> factory) : IClassFixture<ShopApiFactory<Shop.AntiPatterns.Program>>
{
    [Fact]
    public async Task PlaceOrder_persists_the_greeting_format_and_reports_the_update()
    {
        var client = factory.CreateClient();
        var customers = await client.GetFromJsonAsync<List<CustomerSummary>>("/customers");
        var aroha = Assert.Single(customers!, c => c.Email == ShopSeeder.ArohaEmail);
        Assert.Equal(ShopSeeder.ArohaOriginalName, aroha.Name);

        var products = await client.GetFromJsonAsync<List<ProductSummary>>("/products");
        var honey = Assert.Single(products!, p => p.Sku == "HNY-250");

        var response = await client.PostAsJsonAsync("/orders", new PlaceOrderRequest(aroha.Id, [new OrderLineRequest(honey.Id, 2)]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PlaceOrderResponse>();

        Assert.NotNull(body);
        Assert.Equal("Hello AROHA NGATA", body.Greeting);
        Assert.Equal(ShopSeeder.ArohaOriginalName, body.CustomerNameBefore);
        Assert.Equal("AROHA NGATA", body.StoredCustomerName);
        Assert.True(body.CustomerNameChanged);
        Assert.Contains("UPDATE Customers", body.Writes);
        Assert.Contains("INSERT Orders", body.Writes);
        Assert.Contains("UPDATE \"Customers\"", string.Join("\n", body.Statements));

        Assert.True(response.Headers.TryGetValues(WriteLogMiddleware.HeaderName, out var header));
        Assert.Contains("UPDATE Customers", string.Join(", ", header!));

        var after = await client.GetFromJsonAsync<CustomerSummary>($"/customers/{aroha.Id}");
        Assert.Equal("AROHA NGATA", after!.Name);
    }

    [Fact]
    public async Task PlaceOrder_span_names_include_the_customer_update_and_the_order_insert()
    {
        var spans = await TraceCapture.CapturePlaceOrderSpans(factory.CreateClient(), ShopActivity.AntiPatterns);
        Assert.Contains(spans, span => span.Name.Contains("UPDATE Customers", StringComparison.Ordinal));
        Assert.Contains(spans, span => span.Name.Contains("INSERT Orders", StringComparison.Ordinal));
        var update = spans.Single(span => span.Name.Contains("UPDATE Customers", StringComparison.Ordinal));
        Assert.Contains("UPDATE \"Customers\"", update.Statement, StringComparison.Ordinal);
        Assert.Equal(ShopActivity.AntiPatterns, update.App);
        Assert.Equal("place-order", update.Operation);
        Assert.Equal("OrderService", update.Service);
    }

    [Fact]
    public async Task Customer_search_still_returns_Aroha_after_loading_whole_graphs()
    {
        var client = factory.CreateClient();
        var matches = await client.GetFromJsonAsync<List<CustomerSummary>>("/customers?search=aroha");
        var aroha = Assert.Single(matches!);
        Assert.Equal(ShopSeeder.ArohaEmail, aroha.Email);
    }
}

public class BestPracticePlaceOrderTests(ShopApiFactory<Shop.BestPractice.Program> factory) : IClassFixture<ShopApiFactory<Shop.BestPractice.Program>>
{
    [Fact]
    public async Task PlaceOrder_leaves_the_customer_name_and_does_not_update_customers()
    {
        var client = factory.CreateClient();
        var customers = await client.GetFromJsonAsync<List<CustomerSummary>>("/customers");
        var aroha = Assert.Single(customers!, c => c.Email == ShopSeeder.ArohaEmail);
        Assert.Equal(ShopSeeder.ArohaOriginalName, aroha.Name);

        var products = await client.GetFromJsonAsync<List<ProductSummary>>("/products");
        var honey = Assert.Single(products!, p => p.Sku == "HNY-250");
        var existingOrders = await client.GetFromJsonAsync<List<OrderSummary>>("/orders");
        Assert.NotEmpty(existingOrders!);

        var response = await client.PostAsJsonAsync("/orders", new PlaceOrderRequest(aroha.Id, [new OrderLineRequest(honey.Id, 2)]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PlaceOrderResponse>();

        Assert.NotNull(body);
        Assert.Equal("Hello AROHA NGATA", body.Greeting);
        Assert.Equal(ShopSeeder.ArohaOriginalName, body.StoredCustomerName);
        Assert.False(body.CustomerNameChanged);
        Assert.DoesNotContain("UPDATE Customers", body.Writes);
        Assert.Contains("INSERT Orders", body.Writes);
        Assert.DoesNotContain("UPDATE", string.Join("\n", body.Statements), StringComparison.OrdinalIgnoreCase);

        Assert.True(response.Headers.TryGetValues(WriteLogMiddleware.HeaderName, out var header));
        Assert.DoesNotContain("UPDATE Customers", string.Join(", ", header!));

        var after = await client.GetFromJsonAsync<CustomerSummary>($"/customers/{aroha.Id}");
        Assert.Equal(ShopSeeder.ArohaOriginalName, after!.Name);
    }

    [Fact]
    public async Task PlaceOrder_span_names_include_the_insert_and_not_a_customer_update()
    {
        var spans = await TraceCapture.CapturePlaceOrderSpans(factory.CreateClient(), ShopActivity.BestPractice);
        Assert.Contains(spans, span => span.Name.Contains("INSERT Orders", StringComparison.Ordinal));
        Assert.DoesNotContain(spans, span => span.Name.Contains("UPDATE Customers", StringComparison.Ordinal));
        var insert = spans.Single(span => span.Name.Contains("INSERT Orders", StringComparison.Ordinal));
        Assert.Contains("INSERT INTO \"Orders\"", insert.Statement, StringComparison.Ordinal);
        Assert.Equal(ShopActivity.BestPractice, insert.App);
        Assert.Equal("place-order", insert.Operation);
        Assert.Equal("CustomerRepository", insert.Service);
    }

    [Fact]
    public async Task Rename_uses_a_single_update_statement()
    {
        var client = factory.CreateClient();
        var customers = await client.GetFromJsonAsync<List<CustomerSummary>>("/customers");
        var nikau = Assert.Single(customers!, c => c.Email == "nikau.rangi@example.com");

        var response = await client.PatchAsJsonAsync($"/customers/{nikau.Id}", new RenameCustomerRequest("Nikau Te Rangi"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RenameCustomerResponse>();

        Assert.NotNull(body);
        Assert.Equal("Nikau Te Rangi", body.StoredName);
        Assert.Equal(["UPDATE Customers"], body.Writes);
        Assert.Contains("SET \"Name\"", body.Statements.Single(), StringComparison.OrdinalIgnoreCase);
    }
}

file static class TraceCapture
{
    public static async Task<IReadOnlyList<CapturedSpan>> CapturePlaceOrderSpans(HttpClient client, string app)
    {
        var spans = new List<CapturedSpan>();
        var gate = new object();
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.GetTagItem("shop.app")?.ToString() != app)
                    return;

                var statement = activity.GetTagItem("db.statement")?.ToString() ?? "";
                var captured = new CapturedSpan(
                    activity.DisplayName,
                    statement,
                    activity.GetTagItem("shop.app")?.ToString(),
                    activity.GetTagItem("shop.operation")?.ToString(),
                    activity.GetTagItem("shop.service")?.ToString());
                lock (gate)
                    spans.Add(captured);
            }
        };
        ActivitySource.AddActivityListener(listener);

        var customers = await client.GetFromJsonAsync<List<CustomerSummary>>("/customers");
        var person = customers!.First(c => c.Name.Contains("Nikau", StringComparison.OrdinalIgnoreCase) || c.Email.Contains("nikau", StringComparison.OrdinalIgnoreCase));
        if (person.Name == person.Name.Trim().ToUpperInvariant() && person.Email.Contains("nikau", StringComparison.OrdinalIgnoreCase))
        {
            // A previous test may already have upper-cased Nikau. Rename back so the anti-pattern update is a real change.
            var rename = await client.PatchAsJsonAsync($"/customers/{person.Id}", new RenameCustomerRequest("Nikau Rangi"));
            rename.EnsureSuccessStatusCode();
        }

        var products = await client.GetFromJsonAsync<List<ProductSummary>>("/products");
        var honey = products!.Single(p => p.Sku == "HNY-250");
        spans.Clear();
        var response = await client.PostAsJsonAsync("/orders", new PlaceOrderRequest(person.Id, [new OrderLineRequest(honey.Id, 1)]));
        response.EnsureSuccessStatusCode();
        return spans;
    }
}

public sealed record CapturedSpan(string Name, string Statement, string? App, string? Operation, string? Service);
