using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Darp.Luau.Benchmarks.Libraries;

// All libraries are reported in one summary, so that the chart can compare them.
IConfig config = DefaultConfig
    .Instance.AddDiagnoser(MemoryDiagnoser.Default)
    .AddColumn(CategoriesColumn.Default)
    .HideColumns(Column.Type)
    .AddExporter(ChartExporter.Default)
    .WithOptions(ConfigOptions.JoinSummary);

Summary[] summaries = BenchmarkSwitcher
    .FromAssemblies([typeof(DarpLuauBenchmarks).Assembly, typeof(NuLuaBenchmarks).Assembly])
    .Run(args, config)
    .ToArray();

// BenchmarkDotNet still returns normally when a benchmark process crashes. Do not publish an incomplete chart.
if (summaries.Any(summary => summary.BenchmarksCases.Any(benchmark => summary[benchmark]?.ResultStatistics is null)))
    Environment.ExitCode = 1;
