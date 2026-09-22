using System.Diagnostics;
using WasmBenchmarkHistory.Data;

// Regenerates the static trend data consumed by the standalone WASM app's Trends page.
// Reuses the exact same index/report parsing code as the live server app
// (BenchmarkIndexParser / BenchmarkHistoryParser), but runs it once, offline, against a
// curated set of benchmarks, and writes the result as small gzipped JSON files that can be
// served as static assets (no CORS-restricted browser fetch of the live report pages).
//
// Usage: dotnet run --project src/WasmBenchmarkHistory.TrendBuilder -- <output-directory>

if (args.Length < 1)
{
    Console.Error.WriteLine(
        "Usage: WasmBenchmarkHistory.TrendBuilder <output-directory> [benchmark ...]");
    return 1;
}

var outputDirectory = Path.GetFullPath(args[0]);
var curatedBenchmarks = args.Length > 1
    ? args[1..]
    : CuratedBenchmarks.Names;

Console.WriteLine($"Writing trend data for {curatedBenchmarks.Length} curated benchmark(s) to {outputDirectory}");

using var httpClient = new HttpClient();
httpClient.Timeout = TimeSpan.FromSeconds(60);
httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("wasm-benchmark-history-trendbuilder/1.0");

var indexParser = new BenchmarkIndexParser();
var historyParser = new BenchmarkHistoryParser();
var availability = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
var stopwatch = Stopwatch.StartNew();
var failures = 0;

foreach (var run in KnownRunConfigurations.Published)
{
    Console.WriteLine($"Fetching index for {run.DisplayName}...");
    string indexHtml;
    try
    {
        indexHtml = await httpClient.GetStringAsync(run.IndexUri!);
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"  Failed to fetch index for {run.Id}: {exception.Message}");
        failures++;
        continue;
    }

    var links = indexParser.Parse(run.IndexUri!, indexHtml)
        .ToDictionary(link => link.Benchmark, link => link.PageUri, StringComparer.Ordinal);

    foreach (var benchmark in curatedBenchmarks)
    {
        if (!links.TryGetValue(benchmark, out var pageUri))
        {
            Console.WriteLine($"  Skipping '{benchmark}': not present in {run.Id} index.");
            continue;
        }

        BenchmarkHistory history;
        try
        {
            var html = await httpClient.GetStringAsync(pageUri);
            history = historyParser.Parse(benchmark, run, html);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"  Failed to load '{benchmark}' for {run.Id}: {exception.Message}");
            failures++;
            continue;
        }

        var document = TrendHistoryDocument.FromHistory(history);
        var fileName = TrendFileNaming.GetFileName(benchmark);
        var path = Path.Combine(outputDirectory, run.Id, $"{fileName}.json.gz");
        await TrendHistoryDocument.WriteAsync(document, path);

        if (!availability.TryGetValue(benchmark, out var runIds))
        {
            runIds = new HashSet<string>(StringComparer.Ordinal);
            availability.Add(benchmark, runIds);
        }

        runIds.Add(run.Id);
        Console.WriteLine(
            $"  Wrote '{benchmark}' for {run.Id} ({history.Observations.Count} observations).");
    }
}

var catalogDocument = TrendCatalogDocument.FromAvailability(
    availability.ToDictionary(
        pair => pair.Key,
        IReadOnlySet<string> (pair) => pair.Value,
        StringComparer.Ordinal));
await TrendCatalogDocument.WriteAsync(
    catalogDocument, Path.Combine(outputDirectory, "catalog.json.gz"));

Console.WriteLine(
    $"Done in {stopwatch.Elapsed.TotalSeconds:0.0}s. "
    + $"{availability.Count} benchmark(s) covered, {failures} failure(s).");

return failures > 0 && availability.Count == 0 ? 1 : 0;

static class CuratedBenchmarks
{
    // Shared by all four run configurations as of the last curation pass; spread across
    // distinct top-level categories to exercise a variety of shapes/scales in the UI.
    public static readonly string[] Names =
    [
        "ArrayDeAbstraction.foreach_member_array",
        "Benchmark.GetChildKeysTests.AddChainedConfigurationEmpty",
        "BenchmarksGame.Fasta_2.RunBench",
        "Benchstone.BenchI.Pi.Test",
        "BilinearTest.Interpol_AVX",
        "Burgers.Test0",
        "ByteMark.BenchBitOps",
        "Devirtualization.Boxing.InterfaceTypeCheckAndCall",
        "Exceptions.Handling.ThrowAndCatch(kind: Hardware)",
        "FractalPerf.Launch.Test",
        "GuardedDevirtualization.TwoClassVirtual.Call(testInput: pB = 0.00)",
        "HardwareIntrinsics.RayTracer.SoA.Render",
        "IfStatements.IfStatements.Or",
        "Inlining.NoThrowInline.Test",
        "Interop.StructureToPtr.MarshalPtrToStructure",
        "JetStream.Poker.Play",
        "Layout.SearchLoops.LoopGoto",
        "LinqBenchmarks.Count00ForX",
        "Loops.StrengthReduction.SumS3Span",
        "Lowering.InstructionReplacement.TESTtoBT"
    ];
}
