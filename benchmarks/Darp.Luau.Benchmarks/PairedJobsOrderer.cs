using System.Collections.Immutable;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Running;

namespace Darp.Luau.Benchmarks;

/// <summary>
/// Runs all jobs of a benchmark back to back, so that the baseline and the current version see similar machine conditions.
/// </summary>
internal sealed class PairedJobsOrderer : DefaultOrderer
{
    public override IEnumerable<BenchmarkCase> GetExecutionOrder(
        ImmutableArray<BenchmarkCase> benchmarksCase,
        IEnumerable<BenchmarkLogicalGroupRule>? order = null
    ) =>
        base.GetExecutionOrder(benchmarksCase, order)
            .GroupBy(benchmarkCase => benchmarkCase.Descriptor.WorkloadMethod)
            .SelectMany(jobsOfBenchmark => jobsOfBenchmark);
}
