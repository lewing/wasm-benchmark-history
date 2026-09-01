using WasmBenchmarkHistory.Components;
using WasmBenchmarkHistory.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.Configure<BenchmarkDataOptions>(
    builder.Configuration.GetSection(BenchmarkDataOptions.SectionName));
builder.Services.AddSingleton<DiskPageCache>();
builder.Services.AddSingleton<BenchmarkIndexParser>();
builder.Services.AddSingleton<BenchmarkHistoryParser>();
builder.Services.AddHttpClient<CachedPageClient>((services, client) =>
{
    var options = services.GetRequiredService<
        Microsoft.Extensions.Options.IOptions<BenchmarkDataOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("wasm-benchmark-history/1.0");
});
builder.Services.AddScoped<BenchmarkHistoryService>();

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

app.Run();
