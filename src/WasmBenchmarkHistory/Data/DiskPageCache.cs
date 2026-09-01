using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace WasmBenchmarkHistory.Data;

public sealed class DiskPageCache
{
    private readonly string _cacheDirectory;

    public DiskPageCache(IOptions<BenchmarkDataOptions> options)
    {
        _cacheDirectory = ResolveCacheDirectory(options.Value.CacheDirectory);
    }

    public string CacheDirectory => _cacheDirectory;

    public async Task<CachedPage?> TryReadAsync(Uri uri, CancellationToken cancellationToken)
    {
        var path = GetPath(uri);
        if (!File.Exists(path))
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(path, cancellationToken);
        return new CachedPage(content, File.GetLastWriteTimeUtc(path));
    }

    public async Task WriteAsync(Uri uri, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_cacheDirectory);
        var path = GetPath(uri);
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temporaryPath, content, Encoding.UTF8, cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private string GetPath(Uri uri)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)));
        return Path.Combine(_cacheDirectory, hash + ".html");
    }

    private static string ResolveCacheDirectory(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "wasm-benchmark-history",
                "cache");
        }

        var path = Environment.ExpandEnvironmentVariables(configuredPath);
        if (path == "~" || path.StartsWith($"~{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                path.Length == 1 ? string.Empty : path[2..]);
        }

        return Path.GetFullPath(path);
    }
}

public sealed record CachedPage(string Content, DateTime LastWriteUtc);
