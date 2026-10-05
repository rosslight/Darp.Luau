using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Darp.Luau.Benchmarks;

BenchmarkReport[] reports =
[
    .. BenchmarkSwitcher
        .FromAssembly(typeof(BenchmarkConfig).Assembly)
        .Run(args, BenchmarkConfig.Create())
        .SelectMany(summary => summary.Reports),
];

// A slower benchmark never fails the run. Benchmarks that could not run at all are a broken setup and do.
return reports.Length > 0 && !reports.Any(report => report.Success) ? 1 : 0;
