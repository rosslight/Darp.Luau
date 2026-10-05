using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;

namespace Darp.Luau.Benchmarks;

internal static class BenchmarkConfig
{
    /// <summary> Version of the packed baseline library. When unset, only the working tree is benchmarked. </summary>
    private const string BaselineVersionVariable = "DARP_LUAU_BASELINE_VERSION";

    /// <summary> Directory holding the packed baseline library. </summary>
    private const string BaselineFeedVariable = "DARP_LUAU_BASELINE_FEED";

    public static IConfig Create()
    {
        ManualConfig config = DefaultConfig.Instance.AddDiagnoser(MemoryDiagnoser.Default);

        string? baselineVersion = Environment.GetEnvironmentVariable(BaselineVersionVariable);
        if (string.IsNullOrEmpty(baselineVersion))
            return config.AddJob(Job.Default.WithId("Current"));

        string baselineFeed =
            Environment.GetEnvironmentVariable(BaselineFeedVariable)
            ?? throw new InvalidOperationException(
                $"{BaselineFeedVariable} must be set with {BaselineVersionVariable}."
            );
        StatisticalTestColumn timeVerdict = StatisticalTestColumn.CreateDefault();
        return config
            .AddJob(
                Job.Default.WithMsBuildArguments(
                        $"/p:DarpLuauBaselineVersion={baselineVersion}",
                        $"/p:RestoreAdditionalProjectSources=\"{baselineFeed}\""
                    )
                    .WithId("Baseline")
                    .AsBaseline()
            )
            .AddJob(Job.Default.WithId("Current"))
            .AddColumn(timeVerdict)
            .AddExporter(new ComparisonExporter(timeVerdict))
            .HideColumns(Column.Arguments)
            .WithOrderer(new PairedJobsOrderer());
    }
}
