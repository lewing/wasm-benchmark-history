using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class BenchmarkCatalogTests
{
    [Fact]
    public void Constructor_BuildsNamespaceAndTypeCategoryTree()
    {
        var catalog = new BenchmarkCatalog(
        [
            Entry("System.Text.Encoding.GetBytes"),
            Entry("ArrayDeAbstraction.foreach_member_array"),
            Entry("System.Collections.List.Add"),
            Entry("Standalone(parameters: true)")
        ]);

        Assert.Collection(
            catalog.CategoryTree,
            node =>
            {
                Assert.Equal("ArrayDeAbstraction", node.Name);
                Assert.Equal("ArrayDeAbstraction", node.Path);
                Assert.Single(node.Entries);
            },
            node =>
            {
                Assert.Equal("Standalone", node.Name);
                Assert.Single(node.Entries);
            },
            node =>
            {
                Assert.Equal("System", node.Name);
                Assert.Equal(2, node.BenchmarkCount);
                Assert.Collection(
                    node.Children,
                    child =>
                    {
                        Assert.Equal("Collections", child.Name);
                        Assert.Equal("System.Collections", child.Path);
                    },
                    child =>
                    {
                        Assert.Equal("Text", child.Name);
                        Assert.Equal("System.Text", child.Path);
                    });
            });
    }

    private static BenchmarkCatalogEntry Entry(string benchmark) =>
        new(benchmark, new Dictionary<string, Uri>());
}
