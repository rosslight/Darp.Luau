using System.Globalization;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Darp.Luau.Benchmarks;

/// <summary>
/// Writes one row per benchmark that compares the current version with the baseline.
/// Meant to be read in a pull request; the full BenchmarkDotNet report holds the details.
/// </summary>
internal sealed class ComparisonExporter(StatisticalTestColumn timeVerdict) : ExporterBase
{
    private readonly StatisticalTestColumn _timeVerdict = timeVerdict;

    protected override string FileExtension => "md";
    protected override string FileNameSuffix => "-comparison";

    public override void ExportToLog(Summary summary, ILogger logger)
    {
        Comparison[] comparisons =
        [
            .. summary
                .BenchmarksCases.Where(benchmarkCase => !summary.IsBaseline(benchmarkCase))
                .Select(benchmarkCase => Compare(summary, benchmarkCase))
                .OrderBy(comparison => comparison.Benchmark, StringComparer.Ordinal),
        ];

        logger.WriteLine(Summarize(comparisons));
        logger.WriteLine();
        logger.WriteLine("| Benchmark | Baseline | Current | Change | Time | Allocated |");
        logger.WriteLine("| --- | ---: | ---: | ---: | --- | ---: |");
        foreach (Comparison comparison in comparisons)
            logger.WriteLine(comparison.ToMarkdownRow());
    }

    private Comparison Compare(Summary summary, BenchmarkCase current)
    {
        BenchmarkCase? baseline = summary.GetBaseline(summary.GetLogicalGroupKey(current));
        return new Comparison(
            Benchmark: $"{current.Descriptor.Type.Name}.{current.Descriptor.WorkloadMethod.Name}",
            Baseline: baseline is null ? null : Measurement.From(summary, baseline),
            Current: Measurement.From(summary, current),
            TimeVerdict: _timeVerdict.GetValue(summary, current)
        );
    }

    private static string Summarize(Comparison[] comparisons)
    {
        int slower = comparisons.Count(comparison => comparison.IsSlower);
        int faster = comparisons.Count(comparison => comparison.IsFaster);
        int allocatingMore = comparisons.Count(comparison => comparison.AllocatesMore);
        int allocatingLess = comparisons.Count(comparison => comparison.AllocatesLess);
        if (slower + faster + allocatingMore + allocatingLess == 0)
            return $"No significant change in {comparisons.Length} benchmarks.";

        return $"{comparisons.Length} benchmarks: {slower} slower, {faster} faster, "
            + $"{allocatingMore} allocating more, {allocatingLess} allocating less.";
    }

    /// <summary> Mean time and allocation per operation. Null when the benchmark could not be built or run. </summary>
    private sealed record Measurement(double Nanoseconds, long AllocatedBytes)
    {
        public static Measurement? From(Summary summary, BenchmarkCase benchmarkCase)
        {
            BenchmarkReport? report = summary[benchmarkCase];
            if (report?.ResultStatistics is null)
                return null;
            return new Measurement(
                report.ResultStatistics.Mean,
                report.GcStats.GetBytesAllocatedPerOperation(benchmarkCase) ?? 0
            );
        }
    }

    private sealed record Comparison(string Benchmark, Measurement? Baseline, Measurement? Current, string TimeVerdict)
    {
        public bool IsSlower => TimeVerdict == "Slower";
        public bool IsFaster => TimeVerdict == "Faster";
        public bool AllocatesMore => Current?.AllocatedBytes > Baseline?.AllocatedBytes;
        public bool AllocatesLess => Current?.AllocatedBytes < Baseline?.AllocatedBytes;

        public string ToMarkdownRow() =>
            $"| {Benchmark} | {FormatTime(Baseline)} | {FormatTime(Current)} | {FormatChange()} | {FormatVerdict()} | {FormatAllocation()} |";

        private string FormatChange()
        {
            if (Baseline is null || Current is null)
                return "–";
            double percent = (Current.Nanoseconds / Baseline.Nanoseconds - 1) * 100;
            return percent.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%";
        }

        private string FormatVerdict()
        {
            if (Baseline is null || Current is null)
                return "no comparison";
            if (IsSlower)
                return "🔴 slower";
            return IsFaster ? "🟢 faster" : "same";
        }

        private string FormatAllocation()
        {
            if (Current is null)
                return "–";
            if (Baseline is null || Baseline.AllocatedBytes == Current.AllocatedBytes)
                return $"{Current.AllocatedBytes} B";
            string marker = AllocatesMore ? "🔴" : "🟢";
            return $"{marker} {Baseline.AllocatedBytes} B → {Current.AllocatedBytes} B";
        }

        private static string FormatTime(Measurement? measurement)
        {
            if (measurement is null)
                return "–";
            (double value, string unit) = measurement.Nanoseconds switch
            {
                < 1_000 => (measurement.Nanoseconds, "ns"),
                < 1_000_000 => (measurement.Nanoseconds / 1_000, "μs"),
                < 1_000_000_000 => (measurement.Nanoseconds / 1_000_000, "ms"),
                _ => (measurement.Nanoseconds / 1_000_000_000, "s"),
            };
            return value.ToString("0.0", CultureInfo.InvariantCulture) + " " + unit;
        }
    }
}
