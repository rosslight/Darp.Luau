using System.Globalization;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Darp.Luau.Benchmarks.Libraries;

/// <summary> Draws the time and the allocations of all libraries in all scenarios as one SVG chart. </summary>
internal sealed class ChartExporter : ExporterBase
{
    private static readonly Measure[] Measures = [new("Mean time", FormatTime), new("Allocated", FormatBytes)];

    public static ChartExporter Default { get; } = new();

    protected override string FileExtension => "svg";
    protected override string FileNameSuffix => "-chart";

    public override void ExportToLog(Summary summary, ILogger logger)
    {
        Panel[] panels =
        [
            .. Scenario.All.Select(scenario => new Panel(
                scenario,
                [
                    .. Library.All.Select(library =>
                    {
                        BenchmarkReport? report = FindReport(summary, scenario, library);
                        return new Row(library, [MeanTime(report), Allocated(report)]);
                    }),
                ]
            )),
        ];

        Assembly benchmarks = typeof(DarpLuauBenchmarks).Assembly;
        LegendEntry[] legend =
        [
            new(Library.DarpLuau, CommitOf(typeof(LuauState).Assembly), "Luau (native)"),
            new(Library.NuLua, PackageVersion(typeof(NuLuaBenchmarks).Assembly, "NuLua"), "Luau (native)"),
            new(Library.NLua, PackageVersion(benchmarks, "NLua"), "Lua 5.4 (native)"),
            new(Library.LuaCSharp, PackageVersion(benchmarks, "LuaCSharp"), "Lua 5.2 (written in C#)"),
        ];

        var chart = new BarChart(
            title: $"{Library.DarpLuau} and other Lua libraries for .NET",
            subtitle: "Mean time and managed memory allocated per operation. Lower is better.",
            footnotes:
            [
                $"Bars share a scale within one scenario. The factor after a value compares it with {Library.DarpLuau}.",
                DescribeRun(summary),
            ],
            highlightedLibrary: Library.DarpLuau
        );
        logger.Write(chart.Render(Measures, panels, legend));
    }

    private static BenchmarkReport? FindReport(Summary summary, string scenario, string library)
    {
        BenchmarkCase? benchmarkCase = summary.BenchmarksCases.FirstOrDefault(candidate =>
            candidate.Descriptor.WorkloadMethod.GetCustomAttribute<BenchmarkAttribute>()?.Description == scenario
            && candidate.Descriptor.Categories.Contains(library)
        );
        return benchmarkCase is null ? null : summary[benchmarkCase];
    }

    private static double? MeanTime(BenchmarkReport? report) => report?.ResultStatistics?.Mean;

    private static double? Allocated(BenchmarkReport? report) =>
        report?.ResultStatistics is null
            ? null
            : report.GcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase) ?? 0;

    /// <summary> The version of a package referenced by a benchmark project, which the project records as assembly metadata. </summary>
    private static string PackageVersion(Assembly benchmarks, string package) =>
        benchmarks
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(metadata => metadata.Key == $"PackageVersion:{package}")
            .Value
        ?? "";

    /// <summary> The commit Darp.Luau was built from. It is built from source, so it has no release version. </summary>
    private static string CommitOf(Assembly assembly)
    {
        // Reported as "1.0.0+37c7c5d4e0..." when built inside a git repository.
        string[] version = (
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? ""
        ).Split('+');
        return version.Length > 1 ? version[1][..Math.Min(7, version[1].Length)] : "";
    }

    private static string DescribeRun(Summary summary)
    {
        string date = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string processor = summary.HostEnvironmentInfo.Cpu.Value.ProcessorName ?? "unknown processor";
        // Reported as ".NET 10.0.1 (10.0.1, 10.0.125.57005)"; the first part is enough.
        string runtime = summary.HostEnvironmentInfo.RuntimeVersion.Split(" (")[0];
        return $"Measured {date} · {processor} · {runtime}";
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
