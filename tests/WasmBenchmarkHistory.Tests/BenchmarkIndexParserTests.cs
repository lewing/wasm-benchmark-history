using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class BenchmarkIndexParserTests
{
    [Fact]
    public void Parse_PreservesDecodedRelativePathIncludingParameterSlash()
    {
        var parser = new BenchmarkIndexParser();
        var indexUri = new Uri(
            "https://data.example/reports/run%20name/ViperUbuntu/AllTestindex.html");

        var links = parser.Parse(indexUri, Fixture.Read("index.html"));

        Assert.Collection(
            links,
            link => Assert.Equal("Parameterized.Bench(value: \"a/b+c\", items: [1,2])", link.Benchmark),
            link => Assert.Equal("Simple.Namespace.Benchmark", link.Benchmark));
        Assert.All(links, link => Assert.Equal("data.example", link.PageUri.Host));
    }
}
