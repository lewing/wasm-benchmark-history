namespace WasmBenchmarkHistory.Data;

public sealed class CachedPageClient(
    HttpClient httpClient,
    DiskPageCache cache,
    ILogger<CachedPageClient> logger)
{
    public async Task<string> GetStringAsync(
        Uri uri,
        TimeSpan maximumAge,
        CancellationToken cancellationToken)
    {
        var cached = await cache.TryReadAsync(uri, cancellationToken);
        if (cached is not null && DateTime.UtcNow - cached.LastWriteUtc <= maximumAge)
        {
            return cached.Content;
        }

        try
        {
            using var response = await httpClient.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            await cache.WriteAsync(uri, content, cancellationToken);
            return content;
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or TaskCanceledException
            && !cancellationToken.IsCancellationRequested)
        {
            if (cached is not null)
            {
                logger.LogWarning(
                    exception,
                    "Using stale cached benchmark page for {Uri}.",
                    uri);
                return cached.Content;
            }

            throw new BenchmarkDataException(
                BenchmarkDataError.Network,
                $"Could not download benchmark data from {uri.Host}: {exception.Message}",
                exception);
        }
    }
}
