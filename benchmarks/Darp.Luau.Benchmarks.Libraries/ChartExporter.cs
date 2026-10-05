using System.Globalization;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Darp.Luau.Benchmarks.Libraries;

/// <summary> Draws one measure of all libraries in all scenarios as an SVG chart. </summary>
internal sealed class ChartExporter : ExporterBase
{
    private readonly string _fileNameSuffix;
    private readonly string _subtitle;
    private readonly Func<BenchmarkReport, double?> _measure;
    private readonly Func<double, string> _format;

    private ChartExporter(
        string fileNameSuffix,
        string subtitle,
        Func<BenchmarkReport, double?> measure,
        Func<double, string> format
    )
    {
        _fileNameSuffix = fileNameSuffix;
        _subtitle = subtitle;
        _measure = measure;
        _format = format;
    }

    public static ChartExporter Time { get; } =
        new("-time", "Mean time per operation. Lower is better.", report => report.ResultStatistics?.Mean, FormatTime);

    public static ChartExporter Allocations { get; } =
        new(
            "-allocations",
            "Managed memory allocated per operation. Lower is better.",
            report =>
                report.ResultStatistics is null
                    ? null
                    : report.GcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase) ?? 0,
            FormatBytes
        );

    protected override string FileExtension => "svg";
    protected override string FileNameSuffix => _fileNameSuffix;

    public override void ExportToLog(Summary summary, ILogger logger)
    {
        Panel[] panels =
        [
            .. Scenario.All.Select(scenario => new Panel(
                scenario,
                [.. Library.All.Select(library => new Bar(library, Measure(summary, scenario, library)))]
            )),
        ];

        var chart = new BarChart(
            title: $"{Library.DarpLuau} and other Lua libraries for .NET",
            subtitle: _subtitle,
            footnote: DescribeRun(summary),
            highlightedLibrary: Library.DarpLuau
        );
        logger.Write(chart.Render(panels, _format));
    }

    private double? Measure(Summary summary, string scenario, string library)
    {
        BenchmarkCase? benchmarkCase = summary.BenchmarksCases.FirstOrDefault(candidate =>
            candidate.Descriptor.WorkloadMethod.GetCustomAttribute<BenchmarkAttribute>()?.Description == scenario
            && candidate.Descriptor.Categories.Contains(library)
        );
        if (benchmarkCase is null)
            return null;
        BenchmarkReport? report = summary[benchmarkCase];
        return report is null ? null : _measure(report);
    }

    private static string DescribeRun(Summary summary)
    {
        string date = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string processor = summary.HostEnvironmentInfo.Cpu.Value.ProcessorName ?? "unknown processor";
        // Reported as ".NET 10.0.1 (10.0.1, 10.0.125.57005)"; the first part is enough.
        string runtime = summary.HostEnvironmentInfo.RuntimeVersion.Split(" (")[0];
        return $"{date} · {processor} · {runtime} · The factor after a value compares it with {Library.DarpLuau}.";
    }

    private static string FormatTime(double nanoseconds)
    {
        (double value, string unit) = nanoseconds switch
        {
            < 1_000 => (nanoseconds, "ns"),
            < 1_000_000 => (nanoseconds / 1_000, "μs"),
            < 1_000_000_000 => (nanoseconds / 1_000_000, "ms"),
            _ => (nanoseconds / 1_000_000_000, "s"),
        };
        return $"{FormatThreeDigits(value)} {unit}";
    }

    private static string FormatBytes(double bytes)
    {
        if (bytes < 1024)
            return $"{bytes.ToString("0", CultureInfo.InvariantCulture)} B";
        return bytes < 1024 * 1024
            ? $"{FormatThreeDigits(bytes / 1024)} KB"
            : $"{FormatThreeDigits(bytes / (1024 * 1024))} MB";
    }

    private static string FormatThreeDigits(double value)
    {
        string format = value switch
        {
            < 10 => "0.00",
            < 100 => "0.0",
            _ => "0",
        };
        return value.ToString(format, CultureInfo.InvariantCulture);
    }
}
