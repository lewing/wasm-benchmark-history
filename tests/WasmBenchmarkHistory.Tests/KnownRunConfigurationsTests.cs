using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class KnownRunConfigurationsTests
{
    [Fact]
    public void CoreClrR2R_UsesPublishedPerfLabIndex()
    {
        var run = KnownRunConfigurations.Get("coreclr-wasm-r2r");

        Assert.Equal("CoreCLR Wasm R2R", run.DisplayName);
        Assert.Equal(
            "https://pvscmdupload.z22.web.core.windows.net/reports/allTestHistory/refs/heads/main_x64_ubuntu%2022.04_CompilationMode=wasm_R2RType=r2r_RunKind=micro_RuntimeType=coreclr/ViperUbuntu/AllTestindex.html",
            run.IndexUri?.AbsoluteUri);
        Assert.Contains(run, KnownRunConfigurations.Published);
        Assert.Equal(5, KnownRunConfigurations.Published.Count);
        Assert.False(run.IsOptional);
    }

    [Fact]
    public void CoreClrR2RComposite_IsOptionalAndUsesCompositePerfLabIndex()
    {
        var run = KnownRunConfigurations.Get("coreclr-wasm-r2r-composite");

        Assert.Equal("CoreCLR Wasm R2R composite", run.DisplayName);
        Assert.True(run.IsOptional);
        Assert.Equal(
            "https://pvscmdupload.z22.web.core.windows.net/reports/allTestHistory/refs/heads/main_x64_ubuntu%2022.04_CompilationMode=wasm_R2RType=r2r_composite_RunKind=micro_RuntimeType=coreclr/ViperUbuntu/AllTestindex.html",
            run.IndexUri?.AbsoluteUri);
        Assert.Contains(run, KnownRunConfigurations.Published);
        Assert.DoesNotContain(run, KnownRunConfigurations.Required);
        Assert.Equal(4, KnownRunConfigurations.Required.Count);
        Assert.Equal(
            BuildComparison.CoreClrR2RComposite,
            DirectSnapshotHistory.LaneId(run.Id));
        Assert.Equal(run.Id, DirectSnapshotHistory.RunId(BuildComparison.CoreClrR2RComposite));
    }
}
