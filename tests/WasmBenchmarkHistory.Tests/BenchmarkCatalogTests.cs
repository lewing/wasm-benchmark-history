using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class BenchmarkCatalogTests
{
    [Fact]
    public void R2RRunUsesPublishedHistoryAndCanAlsoExposeFallbackAvailability()
    {
        var run = KnownRunConfigurations.Get("coreclr-wasm-r2r");
        var catalog = new BenchmarkCatalog(
        [
            new(
                "N.T.Run",
                new Dictionary<string, Uri>
                {
                    [run.Id] = new Uri(run.IndexUri!, "N.T.Run.html")
                },
                new HashSet<string>([run.Id], StringComparer.Ordinal))
        ]);

        var entry = Assert.Single(catalog.Entries);
        Assert.NotNull(run.IndexUri);
        Assert.Contains("R2RType=r2r", Uri.UnescapeDataString(run.IndexUri.AbsoluteUri));
        Assert.Equal("CoreCLR Wasm R2R", run.DisplayName);
        Assert.True(entry.Pages.ContainsKey(run.Id));
        Assert.Contains(run.Id, entry.DirectRuns);
        Assert.True(entry.IsAvailable(run.Id));
    }

    [Fact]
    public void OptionalRunWithoutDataIsNotExpectedForSharedAvailability()
    {
        var pages = KnownRunConfigurations.Required.ToDictionary(
            run => run.Id, run => new Uri(run.IndexUri!, "N.T.Run.html"));

        var catalog = new BenchmarkCatalog([new("N.T.Run", pages)]);

        var entry = Assert.Single(catalog.Entries);
        Assert.Equal(KnownRunConfigurations.Required, catalog.ActiveRuns);
        Assert.True(entry.IsShared);
        Assert.Equal("Shared by all runs", entry.Availability);
        Assert.Equal(4, entry.ExpectedRunCount);
    }

    [Fact]
    public void OptionalRunWithDataBecomesExpected()
    {
        var composite = KnownRunConfigurations.Get("coreclr-wasm-r2r-composite");
        var required = KnownRunConfigurations.Required.ToDictionary(
            run => run.Id, run => new Uri(run.IndexUri!, "N.T.Run.html"));
        var all = new Dictionary<string, Uri>(required)
        {
            [composite.Id] = new Uri(composite.IndexUri!, "N.T.Run.html")
        };

        var catalog = new BenchmarkCatalog([new("N.T.Run", required), new("N.T.Other", all)]);

        Assert.Equal(KnownRunConfigurations.All, catalog.ActiveRuns);
        var missing = catalog.Find("N.T.Run")!;
        Assert.False(missing.IsShared);
        Assert.Equal("Missing: CoreCLR Wasm R2R composite", missing.Availability);
        Assert.True(catalog.Find("N.T.Other")!.IsShared);
    }

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
