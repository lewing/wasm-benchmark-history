using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WasmBenchmarkHistory.Data;

public sealed record DiscoveredLane(string Id, string DisplayName, string PipelineJobName, int? LogId, string? HelixJobId);
public sealed record DirectRunDiscovery(
    string BuildUrl, BuildProvenance Build, DiscoveredLane[] Lanes, string PerformanceSha);

public sealed class DirectRunAcquirer
{
    private const int ExpectedPartitions = 15;
    private static readonly Regex HelixJobPattern = new(
        @"helix\.dot\.net/api/jobs/(?<id>[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})/",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex CheckoutShaPattern = new(
        @"git checkout[^\r\n]*origin/(?<sha>[0-9a-f]{40})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex PartitionPattern = new(
        @"(?:^|\.)Partition(?<number>\d+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private readonly HelixCli _cli;
    private readonly HttpClient _httpClient;

    public DirectRunAcquirer(HttpClient? httpClient = null, HelixCli? cli = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _cli = cli ?? new HelixCli();
    }

    public async Task<DirectRunDiscovery> DiscoverAsync(string buildIdOrUrl)
    {
        var buildUrl = NormalizeBuildUrl(buildIdOrUrl);
        using var build = await _cli.RunJsonAsync(["azdo", "build", buildUrl, "--json"]);
        ValidateBuild(build.RootElement);
        using var timeline = await _cli.RunJsonAsync(["azdo", "timeline", buildUrl, "--filter", "all", "--json"]);
        var lanes = ParseTimeline(timeline.RootElement);
        var records = timeline.RootElement.GetProperty("records").EnumerateArray().ToArray();

        foreach (var lane in lanes.Where(lane => lane.LogId is not null))
        {
            var log = await _cli.RunTextAsync(["azdo", "log", buildUrl, lane.LogId!.Value.ToString(),
                "--tail-lines", "200"]);
            var jobId = ParseHelixJobId(log);
            var index = Array.IndexOf(lanes, lane);
            lanes[index] = lane with { HelixJobId = jobId };
        }

        var checkout = FindPerformanceCheckout(records, lanes);
        var checkoutLog = await _cli.RunTextAsync(["azdo", "log", buildUrl, checkout.ToString(),
            "--tail-lines", "250"]);
        var performanceSha = ParsePerformanceSha(checkoutLog);
        var sourceVersion = RequiredText(build.RootElement, "SourceVersion");
        var buildNumber = RequiredText(build.RootElement, "BuildNumber");
        var queueTime = build.RootElement.GetProperty("QueueTime").GetDateTimeOffset();
        var provenance = new BuildProvenance(
            build.RootElement.GetProperty("Id").GetInt32().ToString(),
            buildNumber, sourceVersion, performanceSha, queueTime.ToString("O"));
        return new DirectRunDiscovery(buildUrl, provenance, lanes, performanceSha);
    }

    public async Task<BuildSnapshot> AcquireAsync(
        string buildIdOrUrl, string outputPath, string? cacheDirectory = null)
    {
        if (!outputPath.EndsWith(".json.gz", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Snapshot output must end in .json.gz.", nameof(outputPath));
        var discovery = await DiscoverAsync(buildIdOrUrl);
        cacheDirectory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "wasm-benchmark-history", "direct-runs", discovery.Build.BuildId);
        cacheDirectory = Path.GetFullPath(cacheDirectory);
        ValidateCacheDirectory(cacheDirectory);
        Directory.CreateDirectory(cacheDirectory);

        var importLanes = new List<ImportLane>();
        var reportMetadata = new List<ReportProvenance>();
        var caveats = new List<string>
        {
            "Direct Helix snapshot captured manually while the public allTestHistory R2R configuration was unavailable.",
            "Raw authenticated acquisition artifacts remain outside tracked source; this snapshot contains only allowlisted benchmark measurements and curated provenance.",
            "Helix work-item success does not imply complete benchmark usability. Missing, invalid, and duplicate identities remain explicit.",
            "A failed work item remains eligible when complete BenchmarkDotNet report artifacts are available, including upload-only failures."
        };

        foreach (var lane in discovery.Lanes)
        {
            var partitions = new List<ImportPartition>();
            ReportProvenance? reportProvenance = null;
            if (lane.HelixJobId is null)
            {
                caveats.Add($"{lane.DisplayName} was not discovered in the build timeline.");
            }
            else
            {
                var workItems = await LoadWorkItemsAsync(lane.HelixJobId);
                if (workItems.Length == 0)
                    throw new InvalidDataException(
                        $"Helix returned no work items for {lane.Id}; verify authenticated helix.dot.net access.");
                foreach (var workItem in workItems)
                {
                    using var files = await _cli.RunJsonAsync(["files", lane.HelixJobId, workItem.Name, "--json"]);
                    var reportFiles = ParseReportFiles(files.RootElement);
                    var relativeReports = new List<string>();
                    foreach (var reportFile in reportFiles)
                    {
                        var relativePath = Path.Combine(lane.Id, workItem.Partition,
                            SafeReportName(reportFile.Name));
                        var destination = Path.Combine(cacheDirectory, relativePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        await DownloadReportAsync(reportFile.Uri, destination);
                        relativeReports.Add(relativePath.Replace(Path.DirectorySeparatorChar, '/'));
                        var metadata = ReadReportProvenance(destination);
                        reportProvenance ??= metadata;
                        reportMetadata.Add(metadata);
                    }
                    var note = reportFiles.Length == 0
                        ? "No BenchmarkDotNet full report artifacts were available."
                        : workItem.Status == "passed" ? "" :
                            $"Helix work item exited with code {workItem.ExitCode}; available reports were imported.";
                    partitions.Add(new ImportPartition(workItem.Partition, workItem.Status, note,
                        relativeReports.Order(StringComparer.Ordinal).ToArray()));
                }
            }

            var details = reportProvenance ?? new ReportProvenance();
            var laneBuild = discovery.Build;
            importLanes.Add(new ImportLane(new LaneProvenance(
                lane.Id, lane.DisplayName, lane.HelixJobId ?? "unavailable", laneBuild,
                details.TargetRuntimeVersion ?? "unavailable",
                details.V8Version ?? "unavailable",
                details.WorkloadVersion ?? "unavailable",
                LaneConfiguration(lane.Id), ExpectedPartitions,
                details.HostSdkVersion, details.InstallerSdkSha,
                details.BenchmarkDotNetVersion, details.MeasurementConfiguration),
                partitions.OrderBy(partition => PartitionNumber(partition.Name)).ToArray()));
        }

        var finalBuild = ValidateReportMetadata(discovery.Build, reportMetadata);
        importLanes = importLanes.Select(lane => lane with
        {
            Provenance = lane.Provenance with { Build = finalBuild }
        }).ToList();
        var manifest = new BuildImportManifest(
            finalBuild, importLanes.ToArray(), caveats.ToArray(),
            "Direct Helix snapshot", DateTimeOffset.UtcNow);
        var manifestPath = Path.Combine(cacheDirectory, "manifest.json");
        await File.WriteAllTextAsync(manifestPath,
            JsonSerializer.Serialize(manifest, BuildSnapshotImporter.JsonOptions));
        var snapshot = await BuildSnapshotImporter.ImportAsync(manifestPath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        await BuildSnapshotImporter.WriteAsync(snapshot, outputPath);
        return snapshot;
    }

    public async Task<WorkItemResult[]> LoadWorkItemsAsync(string helixJobId)
    {
        using var status = await _cli.RunJsonAsync(["status", helixJobId, "all", "--json"]);
        var workItems = ParseWorkItems(status.RootElement);
        if (workItems.Length == 0)
            throw new InvalidDataException(
                "Helix returned no benchmark partition work items; verify authenticated helix.dot.net access.");
        return workItems;
    }

    public static string NormalizeBuildUrl(string value)
    {
        if (long.TryParse(value, out var id) && id > 0)
            return $"https://dev.azure.com/dnceng/internal/_build/results?buildId={id}";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.Equals("/dnceng/internal/_build/results", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Build must be a positive ID or a dnceng/internal Azure DevOps build URL.");
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        if (!long.TryParse(query["buildId"], out id) || id <= 0)
            throw new ArgumentException("Azure DevOps build URL must contain a positive buildId.");
        return $"https://dev.azure.com/dnceng/internal/_build/results?buildId={id}";
    }

    public static DiscoveredLane[] ParseTimeline(JsonElement timeline)
    {
        if (!timeline.TryGetProperty("records", out var recordsElement) ||
            recordsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Azure DevOps timeline has no records array.");
        var records = recordsElement.EnumerateArray().ToArray();
        var jobs = records.Where(record => Text(record, "type") == "Job")
            .Select(record => (Record: record, Lane: TryMapLane(Text(record, "name"))))
            .Where(value => value.Lane is not null).ToArray();
        var duplicates = jobs.GroupBy(value => value.Lane!.Value.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (duplicates.Length > 0)
            throw new InvalidDataException($"Duplicate Wasm pipeline jobs: {string.Join(", ", duplicates)}.");

        return LaneDefinitions.Select(definition =>
        {
            var job = jobs.SingleOrDefault(value => value.Lane!.Value.Id == definition.Id);
            if (job.Lane is null)
                return new DiscoveredLane(definition.Id, definition.DisplayName, definition.Pattern, null, null);
            var jobId = RequiredText(job.Record, "id");
            var tasks = records.Where(record => Text(record, "parentId") == jobId &&
                (Text(record, "name")?.StartsWith("Send job to Helix", StringComparison.Ordinal) ?? false)).ToArray();
            if (tasks.Length == 0)
                return new DiscoveredLane(
                    definition.Id, definition.DisplayName, RequiredText(job.Record, "name"), null, null);
            if (tasks.Length != 1 || !tasks[0].TryGetProperty("log", out var log) ||
                !log.TryGetProperty("id", out var logId) || !logId.TryGetInt32(out var id))
                throw new InvalidDataException($"Expected one logged Send job to Helix task for {definition.Id}.");
            return new DiscoveredLane(definition.Id, definition.DisplayName,
                RequiredText(job.Record, "name"), id, null);
        }).ToArray();
    }

    public static string ParseHelixJobId(string log)
    {
        var matches = HelixJobPattern.Matches(log).Select(match => match.Groups["id"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return matches.Length == 1 ? matches[0].ToLowerInvariant() :
            throw new InvalidDataException("Expected exactly one Helix job ID in the send-job log.");
    }

    public static string ParsePerformanceSha(string log)
    {
        var matches = CheckoutShaPattern.Matches(log).Select(match => match.Groups["sha"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return matches.Length == 1 ? matches[0].ToLowerInvariant() :
            throw new InvalidDataException("Expected exactly one dotnet-performance checkout SHA.");
    }

    public static WorkItemResult[] ParseWorkItems(JsonElement status)
    {
        var results = new List<WorkItemResult>();
        AddWorkItems(status, "passed", "passed", results);
        AddWorkItems(status, "failed", "failed", results);
        return results.OrderBy(item => PartitionNumber(item.Partition)).ToArray();
    }

    public static ReportFile[] ParseReportFiles(JsonElement files)
    {
        var values = new List<ReportFile>();
        foreach (var group in new[] { "other", "testResults", "binlogs" })
        {
            if (!files.TryGetProperty(group, out var array) || array.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var file in array.EnumerateArray())
            {
                var name = Text(file, "Name");
                var uri = Text(file, "Uri");
                if (name is not null && uri is not null && IsReportName(name) &&
                    Uri.TryCreate(uri, UriKind.Absolute, out var parsed) &&
                    parsed.Scheme == Uri.UriSchemeHttps &&
                    (parsed.Host.EndsWith(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase) ||
                     parsed.Host.Equals("helix.dot.net", StringComparison.OrdinalIgnoreCase)))
                    values.Add(new ReportFile(name, uri));
            }
        }
        var combined = values.Where(value => Regex.IsMatch(Path.GetFileName(value.Name),
            @"^Partition\d+-combined-perf-lab-report\.json$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1))).ToList();
        if (combined.Count > 0)
            values = combined;
        var duplicates = values.GroupBy(value => value.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (duplicates.Length > 0)
            throw new InvalidDataException(
                $"Helix returned duplicate report names: {string.Join(", ", duplicates)}.");
        return values.OrderBy(value => value.Name, StringComparer.Ordinal).ToArray();
    }

    private static readonly (string Id, string DisplayName, string Pattern)[] LaneDefinitions =
    [
        ("mono-interpreter", "Mono interpreter", "Performance micro wasm wasm v8 linux"),
        ("mono-aot", "Mono AOT", "Performance micro wasm aot v8 linux"),
        ("coreclr-interpreter", "CoreCLR interpreter", "Performance micro wasm_coreclr wasm coreclr_v8 linux"),
        ("coreclr-r2r", "CoreCLR R2R", "Performance micro wasm_coreclr wasm coreclr_r2r_v8 linux")
    ];

    private static (string Id, string DisplayName)? TryMapLane(string? name)
    {
        if (name is null)
            return null;
        foreach (var definition in LaneDefinitions.OrderByDescending(value => value.Pattern.Length))
            if (name.StartsWith(definition.Pattern, StringComparison.Ordinal))
                return (definition.Id, definition.DisplayName);
        return null;
    }

    private static int FindPerformanceCheckout(JsonElement[] records, DiscoveredLane[] lanes)
    {
        foreach (var lane in lanes.Where(lane => lane.LogId is not null))
        {
            var job = records.Single(record => Text(record, "type") == "Job" &&
                Text(record, "name") == lane.PipelineJobName);
            var task = records.SingleOrDefault(record => Text(record, "parentId") == Text(job, "id") &&
                Text(record, "name") == "Checkout dotnet-performance@main to s/performance");
            if (task.ValueKind == JsonValueKind.Object && task.TryGetProperty("log", out var log) &&
                log.TryGetProperty("id", out var id) && id.TryGetInt32(out var logId))
                return logId;
        }
        throw new InvalidDataException("No dotnet-performance checkout task was found in a Wasm lane.");
    }

    private static void ValidateBuild(JsonElement build)
    {
        if (build.GetProperty("DefinitionId").GetInt32() != 702 ||
            RequiredText(build, "DefinitionName") != "dotnet-runtime-perf" ||
            RequiredText(build, "SourceBranch") != "refs/heads/main")
            throw new InvalidDataException("Only dotnet-runtime-perf definition 702 main builds are supported.");
        var source = RequiredText(build, "SourceVersion");
        if (source.Length != 40 || !source.All(char.IsAsciiHexDigit))
            throw new InvalidDataException("Build source version is not a full commit SHA.");
    }

    private static void AddWorkItems(
        JsonElement status, string property, string state, List<WorkItemResult> results)
    {
        if (!status.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
            return;
        foreach (var item in array.EnumerateArray())
        {
            var name = RequiredText(item, "Name");
            var match = PartitionPattern.Match(name);
            if (!match.Success)
                continue;
            var exitCode = item.TryGetProperty("ExitCode", out var exit) && exit.TryGetInt32(out var value)
                ? value : (int?)null;
            results.Add(new WorkItemResult(name, $"Partition{int.Parse(match.Groups["number"].Value)}",
                state, exitCode));
        }
    }

    private static bool IsReportName(string name) =>
        name.EndsWith("-report-full.json", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith("-report-full-compressed.json", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith("-report-full.json.gz", StringComparison.OrdinalIgnoreCase) ||
        Regex.IsMatch(Path.GetFileName(name),
            @"^Partition\d+-combined-perf-lab-report\.json$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

    private async Task DownloadReportAsync(string uriText, string destination)
    {
        if (File.Exists(destination))
            return;
        using var response = await _httpClient.GetAsync(new Uri(uriText), HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
            throw new IOException($"Helix report download failed with HTTP {(int)response.StatusCode}.");
        var temporary = destination + ".download";
        await using (var source = await response.Content.ReadAsStreamAsync())
        await using (var target = File.Create(temporary))
            await source.CopyToAsync(target);
        try
        {
            await ValidateOrExpandJsonAsync(temporary, destination);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static async Task ValidateOrExpandJsonAsync(string source, string destination)
    {
        await using var input = File.OpenRead(source);
        Stream jsonStream = input;
        if (input.Length >= 2)
        {
            var first = input.ReadByte();
            var second = input.ReadByte();
            input.Position = 0;
            if (first == 0x1f && second == 0x8b)
                jsonStream = new GZipStream(input, CompressionMode.Decompress);
        }
        await using (jsonStream == input ? Stream.Null : jsonStream)
        {
            using var document = await JsonDocument.ParseAsync(jsonStream);
            var supported = document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("Benchmarks", out var benchmarks) &&
                    benchmarks.ValueKind == JsonValueKind.Array ||
                document.RootElement.ValueKind == JsonValueKind.Array;
            if (!supported)
                throw new InvalidDataException(
                    "Downloaded artifact is not a BenchmarkDotNet full report or combined perf-lab report.");
            await File.WriteAllBytesAsync(destination,
                JsonSerializer.SerializeToUtf8Bytes(document.RootElement));
        }
    }

    private static ReportProvenance ReadReportProvenance(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var combined = document.RootElement.ValueKind == JsonValueKind.Array;
        var root = combined &&
            document.RootElement.GetArrayLength() > 0
            ? document.RootElement[0]
            : document.RootElement;
        var build = root.TryGetProperty("build", out var buildValue) ? buildValue : default;
        var run = root.TryGetProperty("run", out var runValue) ? runValue : default;
        return new ReportProvenance(
            FindString(root, "TargetRuntimeVersion", "RuntimePackageVersion", "PERFLAB_WASM_PACKAGE_VERSION"),
            FindString(root, "V8Version"),
            FindString(root, "WorkloadVersion", "WorkloadManifestVersion"),
            FindString(root, "DotNetSdkVersion", "sdkVersion"),
            FindSha(root, "InstallerHash", "InstallerSdkSha"),
            FindString(root, "BenchmarkDotNetVersion", "BenchmarkDotNetCaption"),
            null,
            Text(build, "gitHash"),
            Text(run, "perfRepoHash"),
            Text(build, "buildName"),
            Text(build, "timeStamp"),
            combined);
    }

    private static string? FindString(JsonElement element, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (names.Contains(property.Name, StringComparer.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(property.Value.GetString()))
                    return property.Value.GetString();
                var nested = FindString(property.Value, names);
                if (nested is not null)
                    return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                var nested = FindString(child, names);
                if (nested is not null)
                    return nested;
            }
        }
        return null;
    }

    private static string? FindSha(JsonElement element, params string[] names)
    {
        var value = FindString(element, names);
        return value is { Length: 40 } && value.All(char.IsAsciiHexDigit) ? value.ToLowerInvariant() : null;
    }

    private static string LaneConfiguration(string id) => id switch
    {
        "mono-interpreter" => "CompilationMode=wasm;RunKind=micro",
        "mono-aot" => "CompilationMode=wasm;RunKind=micro;AOT=true",
        "coreclr-interpreter" => "CompilationMode=wasm;RunKind=micro;RuntimeType=coreclr",
        "coreclr-r2r" => "CompilationMode=wasm;RunKind=micro;RuntimeType=coreclr;R2RType=r2r",
        _ => throw new InvalidDataException($"Unsupported lane '{id}'.")
    };

    private static string SafeReportName(string name)
    {
        var fileName = Path.GetFileName(name.Replace('\\', '/'));
        if (fileName.Length == 0 || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("Helix report has an invalid file name.");
        return fileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^3] : fileName;
    }

    public static void ValidateCacheDirectory(string cacheDirectory)
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")) &&
            !File.Exists(Path.Combine(directory.FullName, ".git")))
            directory = directory.Parent;
        if (directory is null)
            return;
        var repository = directory.FullName.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var cache = cacheDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (cache.StartsWith(repository, StringComparison.Ordinal))
            throw new ArgumentException("Raw acquisition cache must be outside the Git repository.");
    }

    private static BuildProvenance ValidateReportMetadata(
        BuildProvenance build,
        IReadOnlyCollection<ReportProvenance> metadata)
    {
        if (metadata.Count == 0)
            return build;
        if (metadata.Any(value => value.IsCombined &&
            (value.RuntimeSha is null || value.PerformanceSha is null ||
             value.BuildNumber is null || value.BuildTimestamp is null)))
            throw new InvalidDataException("A combined perf-lab report is missing required build provenance.");
        var runtimeShas = metadata.Select(value => value.RuntimeSha).Where(value => value is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var performanceShas = metadata.Select(value => value.PerformanceSha).Where(value => value is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var buildNumbers = metadata.Select(value => value.BuildNumber).Where(value => value is not null)
            .Distinct(StringComparer.Ordinal).ToArray();
        var timestamps = metadata.Select(value => value.BuildTimestamp).Where(value => value is not null)
            .Select(value => DateTimeOffset.Parse(value!,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime())
            .Distinct().ToArray();
        if (runtimeShas.Length != 1 || runtimeShas[0] != build.RuntimeSha ||
            performanceShas.Length != 1 || performanceShas[0] != build.PerformanceSha ||
            buildNumbers.Length != 1 || buildNumbers[0] != build.BuildNumber ||
            timestamps.Length != 1)
            throw new InvalidDataException(
                "Perf-lab report provenance does not match the selected Azure DevOps build.");
        return build with { SourceDate = timestamps[0].ToString("O") };
    }

    private static int PartitionNumber(string name) =>
        int.TryParse(name.AsSpan("Partition".Length), out var number) ? number : int.MaxValue;

    private static string RequiredText(JsonElement element, string property) =>
        Text(element, property) is { Length: > 0 } value ? value :
            throw new InvalidDataException($"Required property '{property}' is missing.");

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private sealed record ReportProvenance(
        string? TargetRuntimeVersion = null,
        string? V8Version = null,
        string? WorkloadVersion = null,
        string? HostSdkVersion = null,
        string? InstallerSdkSha = null,
        string? BenchmarkDotNetVersion = null,
        string? MeasurementConfiguration = null,
        string? RuntimeSha = null,
        string? PerformanceSha = null,
        string? BuildNumber = null,
        string? BuildTimestamp = null,
        bool IsCombined = false);
}

public sealed record WorkItemResult(
    string Name, string Partition, string Status, int? ExitCode);
public sealed record ReportFile(string Name, string Uri);

public class HelixCli
{
    public virtual async Task<JsonDocument> RunJsonAsync(IReadOnlyList<string> arguments)
    {
        var output = await RunAsync(arguments);
        var start = output.IndexOf('{');
        if (start < 0)
            throw new InvalidDataException("Helix CLI returned no JSON object.");
        try
        {
            return JsonDocument.Parse(output[start..]);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Helix CLI returned malformed JSON.", exception);
        }
    }

    public virtual Task<string> RunTextAsync(IReadOnlyList<string> arguments) => RunAsync(arguments);

    private static async Task<string> RunAsync(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetTempPath(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[] { "dnx", "-y", "lewing.helix.mcp", "--" }.Concat(arguments))
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not start the Helix CLI.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await stderr;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Helix CLI command failed with exit code {process.ExitCode}.");
        return await stdout;
    }
}
