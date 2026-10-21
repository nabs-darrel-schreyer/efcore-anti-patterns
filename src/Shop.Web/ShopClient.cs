using System.Net.Http.Json;
using System.Text.Json;
using Shop.Shared;

namespace Shop.Web;

public sealed class ShopClient(IHttpClientFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<IReadOnlyList<CustomerSummary>> CustomersAsync(string app, CancellationToken ct = default) =>
        GetAsync<CustomerSummary>(app, "/customers", ct);

    public Task<IReadOnlyList<ProductSummary>> ProductsAsync(string app, CancellationToken ct = default) =>
        GetAsync<ProductSummary>(app, "/products", ct);

    public Task<IReadOnlyList<OrderSummary>> OrdersAsync(string app, CancellationToken ct = default) =>
        GetAsync<OrderSummary>(app, "/orders", ct);

    public async Task<PlaceOrderResponse> PlaceAsync(string app, PlaceOrderRequest request, CancellationToken ct = default)
    {
        using var client = Create(app);
        using var response = await client.PostAsJsonAsync("/orders", request, Json, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode}: {body}");

        return JsonSerializer.Deserialize<PlaceOrderResponse>(body, Json)
            ?? throw new InvalidOperationException("The API returned an empty order response.");
    }

    private async Task<IReadOnlyList<T>> GetAsync<T>(string app, string path, CancellationToken ct)
    {
        using var client = Create(app);
        var items = await client.GetFromJsonAsync<List<T>>(path, Json, ct);
        return items ?? [];
    }

    private HttpClient Create(string app) => factory.CreateClient(app);
}
