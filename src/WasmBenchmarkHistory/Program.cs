using WasmBenchmarkHistory.Components;
using WasmBenchmarkHistory.Data;

if (args.Length > 0 && args[0] == "--discover-build")
{
    if (args.Length != 2)
        throw new ArgumentException("Usage: --discover-build <internal-build-url-or-id>");
    var discovery = await new DirectRunAcquirer().DiscoverAsync(args[1]);
    Console.WriteLine($"Build {discovery.Build.BuildId} ({discovery.Build.BuildNumber})");
    Console.WriteLine($"Runtime {discovery.Build.RuntimeSha}; performance {discovery.Build.PerformanceSha}");
    foreach (var lane in discovery.Lanes)
    {
        if (lane.HelixJobId is null)
        {
            Console.WriteLine($"{lane.Id}: missing");
            continue;
        }
        var workItems = await new DirectRunAcquirer().LoadWorkItemsAsync(lane.HelixJobId);
        Console.WriteLine($"{lane.Id}: {lane.HelixJobId}, {workItems.Length} partitions, " +
            $"{workItems.Count(item => item.Status == "passed")} passed, " +
            $"{workItems.Count(item => item.Status == "failed")} failed");
    }
    return;
}

if (args.Length > 0 && args[0] == "--acquire-build")
{
    if (args.Length is not (3 or 5) || args.Length == 5 && args[3] != "--cache")
        throw new ArgumentException(
            "Usage: --acquire-build <internal-build-url-or-id> <output.json.gz> [--cache <directory>]");
    var snapshot = await new DirectRunAcquirer().AcquireAsync(
        args[1], args[2], args.Length == 5 ? args[4] : null);
    var comparison = BuildComparison.Analyze(snapshot);
    Console.WriteLine($"Build {snapshot.Build.BuildId}: {comparison.Rows.Length} identities, " +
        $"{comparison.Common.Length} valid four-runtime matches.");
    foreach (var coverage in comparison.Coverage)
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(coverage));
    return;
}

if (args.Length > 0 && args[0] == "--build-direct-history")
{
    if (args.Length < 4 || !int.TryParse(args[2], out var retention))
        throw new ArgumentException(
            "Usage: --build-direct-history <output.json.gz> <retention> <full-snapshot.json.gz> [...]");
    var archive = await DirectHistoryRefresh.BuildAsync(
        args.Skip(3), args[1], retention, DateTimeOffset.UtcNow);
    Console.WriteLine($"Direct history: {archive.Builds.Length} retained builds, " +
        $"{archive.Exclusions.Length} excluded or pruned.");
    foreach (var exclusion in archive.Exclusions)
        Console.WriteLine($"Excluded {exclusion.BuildId}: {exclusion.Reason}");
    return;
}

if (args.Length > 0 && args[0] == "--refresh-direct-history")
{
    if (args.Length < 4 || !int.TryParse(args[3], out var retention))
        throw new ArgumentException(
            "Usage: --refresh-direct-history <output.json.gz> <full-archive-directory> <retention> [build-id-or-url ...]");
    var archive = await DirectHistoryRefresh.RefreshAsync(
        args.Skip(4), args[2], args[1], retention);
    Console.WriteLine($"Direct history: {archive.Builds.Length} retained builds, " +
        $"{archive.Exclusions.Length} excluded or pruned.");
    foreach (var exclusion in archive.Exclusions)
        Console.WriteLine($"Excluded {exclusion.BuildId}: {exclusion.Reason}");
    return;
}

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
builder.Services.Configure<GitHubIssueOptions>(
    builder.Configuration.GetSection(GitHubIssueOptions.SectionName));
builder.Services.AddSingleton<DiskPageCache>();
builder.Services.AddSingleton<BenchmarkIndexParser>();
builder.Services.AddSingleton<BenchmarkHistoryParser>();
builder.Services.AddSingleton<ChangeSetIssueParser>();
builder.Services.AddHttpClient<CachedPageClient>((services, client) =>
{
    var options = services.GetRequiredService<
        Microsoft.Extensions.Options.IOptions<BenchmarkDataOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("wasm-benchmark-history/1.0");
});
builder.Services.AddScoped<BenchmarkHistoryService>();
builder.Services.AddHttpClient<PerfAutofilingIssueClient>((services, client) =>
{
    var options = services.GetRequiredService<
        Microsoft.Extensions.Options.IOptions<GitHubIssueOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("wasm-benchmark-history/1.0");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false
});
builder.Services.AddSingleton<BuildSnapshotStore>();
builder.Services.AddSingleton<IBuildSnapshotStore>(
    services => services.GetRequiredService<BuildSnapshotStore>());
builder.Services.AddSingleton<BundledDirectHistoryProvider>();
builder.Services.AddSingleton<IBenchmarkHistoryProvider>(
    services => services.GetRequiredService<BundledDirectHistoryProvider>());
builder.Services.AddSingleton<IDirectHistoryArchiveSource>(
    services => services.GetRequiredService<BundledDirectHistoryProvider>());

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
