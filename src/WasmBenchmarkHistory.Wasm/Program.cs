using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using WasmBenchmarkHistory.Data;
using WasmBenchmarkHistory.Wasm;
using WasmBenchmarkHistory.Wasm.Data;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});

builder.Services.AddScoped<WasmBuildSnapshotStore>();
builder.Services.AddScoped<IBuildSnapshotStore>(
    services => services.GetRequiredService<WasmBuildSnapshotStore>());
builder.Services.AddScoped<WasmDirectHistoryProvider>();
builder.Services.AddScoped<IBenchmarkHistoryProvider>(
    services => services.GetRequiredService<WasmDirectHistoryProvider>());
builder.Services.AddScoped<IDirectHistoryArchiveSource>(
    services => services.GetRequiredService<WasmDirectHistoryProvider>());
builder.Services.AddScoped<WasmTrendHistoryProvider>();
builder.Services.AddScoped<IBenchmarkHistoryProvider>(
    services => services.GetRequiredService<WasmTrendHistoryProvider>());
builder.Services.AddScoped<IBenchmarkHistoryOrchestrator, PrebuiltBenchmarkHistoryOrchestrator>();

await builder.Build().RunAsync();
