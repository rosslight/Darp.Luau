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
        Job job = Job.Default.WithWarmupCount(5).WithIterationCount(15);
        ManualConfig config = DefaultConfig.Instance.AddDiagnoser(MemoryDiagnoser.Default);

        string? baselineVersion = Environment.GetEnvironmentVariable(BaselineVersionVariable);
        if (string.IsNullOrEmpty(baselineVersion))
            return config.AddJob(job.WithId("Current"));

        string baselineFeed =
            Environment.GetEnvironmentVariable(BaselineFeedVariable)
            ?? throw new InvalidOperationException(
                $"{BaselineFeedVariable} must be set with {BaselineVersionVariable}."
            );
        StatisticalTestColumn timeVerdict = StatisticalTestColumn.CreateDefault();
        return config
            .AddJob(
                job.WithMsBuildArguments(
                        $"/p:DarpLuauBaselineVersion={baselineVersion}",
                        $"/p:RestoreAdditionalProjectSources=\"{baselineFeed}\""
                    )
                    .WithId("Baseline")
                    .AsBaseline()
            )
            .AddJob(job.WithId("Current"))
            .AddColumn(timeVerdict)
            .AddExporter(new ComparisonExporter(timeVerdict))
            .HideColumns(Column.Arguments)
            .WithOrderer(new PairedJobsOrderer());
    }
}
