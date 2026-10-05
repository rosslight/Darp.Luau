# Benchmarks

Benchmarks for the core paths of Darp.Luau, built with [BenchmarkDotNet](https://benchmarkdotnet.org/).

## Run against the working tree

```bash
dotnet run -c Release --project benchmarks/Darp.Luau.Benchmarks -- --filter '*'
```

## Compare against another commit

The comparison measures two versions of the library with the same benchmark code in one run.
The other version is packed into a local feed and selected through two environment variables.

```bash
# Outside this repository: BenchmarkDotNet must not find a second copy of the benchmark project below the root.
git worktree add ../darp-luau-baseline origin/main
dotnet pack ../darp-luau-baseline/src/Darp.Luau/Darp.Luau.csproj -c Release \
  -o tmp/benchmark-feed -p:Version=0.0.0-baseline.1

export DARP_LUAU_BASELINE_VERSION=0.0.0-baseline.1
export DARP_LUAU_BASELINE_FEED="$PWD/tmp/benchmark-feed"
dotnet run -c Release --project benchmarks/Darp.Luau.Benchmarks -- --filter '*' --join
```

NuGet caches packages by version. Use a new version each time you pack a different baseline.

The run writes two reports to `BenchmarkDotNet.Artifacts/results`:

- `*-report-comparison.md` has one row per benchmark: baseline, current, the change, and whether the time difference is statistically significant.
- `*-report-github.md` is the full BenchmarkDotNet table. The `Baseline` rows are the other commit and the `Current` rows are the working tree.

If the baseline does not compile against the current benchmarks, its values are missing and the row says `no comparison`.

## In CI

The `Benchmark` workflow runs this comparison for every pull request against its target branch.
It posts the one-row-per-benchmark table as a comment, with the full report folded below it, and updates that comment on every push.
A slower benchmark never fails the build; the job only fails when the benchmarks could not run at all. Timings on shared runners are noisy, so trust the verdict more than the percentage. Allocations are exact.
