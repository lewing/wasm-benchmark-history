using Microsoft.Extensions.Options;
using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class DiskPageCacheTests
{
    [Theory]
    [InlineData("~/wasm-history-cache-test/nested")]
    [InlineData(@"~\wasm-history-cache-test\nested")]
    public void CacheDirectory_ExpandsEitherHomeSeparator(string configuredPath)
    {
        var cache = new DiskPageCache(Options.Create(new BenchmarkDataOptions
        {
            CacheDirectory = configuredPath
        }));

        Assert.Equal(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "wasm-history-cache-test",
                "nested"),
            cache.CacheDirectory);
    }
}
