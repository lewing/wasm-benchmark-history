using WasmBenchmarkHistory.Components;
using WasmBenchmarkHistory.Data;

if (args.Length > 0 && args[0] == "--import-build")
{
    if (args.Length != 3)
        throw new ArgumentException("Usage: --import-build <manifest.json> <output.json.gz>");
    var snapshot = await BuildSnapshotImporter.ImportAsync(args[1]);
    await BuildSnapshotImporter.WriteAsync(snapshot, args[2]);
    var comparison = BuildComparison.Analyze(snapshot);
    Console.WriteLine($"Build {snapshot.Build.BuildId}: {comparison.Rows.Length} identities, " +
        $"{comparison.Common.Length} valid four-runtime matches.");
    foreach (var coverage in comparison.Coverage)
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(coverage));
    foreach (var pair in BuildComparison.Summarize(comparison.Rows))
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(pair));
    return;
}

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
builder.Services.AddSingleton<BuildSnapshotStore>();

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
