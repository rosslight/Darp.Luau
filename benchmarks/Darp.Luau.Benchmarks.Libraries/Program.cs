using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Running;
using Darp.Luau.Benchmarks.Libraries;

// All libraries are reported in one summary, so that the charts can compare them.
IConfig config = DefaultConfig
    .Instance.AddDiagnoser(MemoryDiagnoser.Default)
    .AddColumn(CategoriesColumn.Default)
    .HideColumns(Column.Type)
    .AddExporter(ChartExporter.Time, ChartExporter.Allocations)
    .WithOptions(ConfigOptions.JoinSummary);

BenchmarkSwitcher
    .FromAssemblies([typeof(DarpLuauBenchmarks).Assembly, typeof(NuLuaBenchmarks).Assembly])
    .Run(args, config);
