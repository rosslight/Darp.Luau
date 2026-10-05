using BenchmarkDotNet.Running;
using Darp.Luau.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(BenchmarkConfig).Assembly).Run(args, BenchmarkConfig.Create());
