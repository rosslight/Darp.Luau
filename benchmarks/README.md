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
git worktree add tmp/benchmark-baseline origin/main
dotnet pack tmp/benchmark-baseline/src/Darp.Luau/Darp.Luau.csproj -c Release \
  -o tmp/benchmark-feed -p:Version=0.0.0-baseline.1

export DARP_LUAU_BASELINE_VERSION=0.0.0-baseline.1
export DARP_LUAU_BASELINE_FEED="$PWD/tmp/benchmark-feed"
dotnet run -c Release --project benchmarks/Darp.Luau.Benchmarks -- --filter '*' --join
```

NuGet caches packages by version. Use a new version each time you pack a different baseline.

The `Baseline` rows are the other commit and the `Current` rows are the working tree.
`Ratio` and `Alloc Ratio` compare the two, and the `MannWhitney` column says whether a time difference is statistically significant.
If the baseline does not compile against the current benchmarks, its rows show `NA`.

## In CI

The `Benchmark` workflow runs this comparison for every pull request against its target branch and posts the table as a comment.
It never fails the build. Timings on shared runners are noisy, so read the `MannWhitney` verdict before the ratio. Allocations are exact.
