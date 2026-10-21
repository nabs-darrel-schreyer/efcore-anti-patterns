using Shop.Shared;
using Shop.Web;
using Shop.Web.Components;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddScoped<ShopClient>();
builder.Services.AddHttpClient(ShopActivity.AntiPatterns, client =>
    client.BaseAddress = new Uri($"https+http://{ShopActivity.AntiPatterns}"));
builder.Services.AddHttpClient(ShopActivity.BestPractice, client =>
    client.BaseAddress = new Uri($"https+http://{ShopActivity.BestPractice}"));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapDefaultEndpoints();
app.Run();
