# Benchmarks

Two sets of benchmarks, both built with [BenchmarkDotNet](https://benchmarkdotnet.org/):

- `Darp.Luau.Benchmarks` measures the core paths of Darp.Luau and compares a change with its target branch.
- `Darp.Luau.Benchmarks.Libraries` compares Darp.Luau with other Lua libraries for .NET.

## Core paths

### Run against the working tree

```bash
dotnet run -c Release --project benchmarks/Darp.Luau.Benchmarks -- --filter '*'
```

### Compare against another commit

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

### In CI

The `Benchmark` workflow runs this comparison for every pull request against its target branch.
It posts the one-row-per-benchmark table as a comment, with the full report folded below it, and updates that comment on every push.
A slower benchmark never fails the build; the job only fails when the benchmarks could not run at all. Timings on shared runners are noisy, so trust the verdict more than the percentage. Allocations are exact.

## Comparison with other libraries

```bash
dotnet run -c Release --project benchmarks/Darp.Luau.Benchmarks.Libraries -- --filter '*'
```

Darp.Luau is compared with:

- [NuLua](https://github.com/nuskey8/NuLua) (Unified Lua5.x/LuaJIT/Luau bindings for .NET and Unity) - tested v0.1.0, Luau
- [NLua](https://github.com/NLua/NLua) (dynamic bridge between Lua world and the .NET) - tested v1.7.9, Lua 5.4
- [Lua-CSharp](https://github.com/nuskey8/Lua-CSharp) (High performance Lua interpreter implemented in C# for .NET and Unity) - tested v0.5.7, Lua 5.2 in .NET

Every library runs the same five scenarios from `Scenario.cs`:

| Scenario | What is measured |
| --- | --- |
| Call a Lua function from C# | One call of a cached `add` function, and reading the result. |
| Call a C# function from Lua | A Lua loop calling a managed `add` 1000 times, divided by 1000. |
| Set and get a table field | Overwriting one field of an existing table and reading it back. |
| Run fib(20) in Lua | Executing an already compiled recursive function. |
| Create and dispose a state | Creating a state with the library's default standard libraries, and disposing it. |

Rules that keep it comparable:

- All numbers are passed as floating-point values, so Lua 5.4 does not switch to integer arithmetic.
- Compiling the script and looking up functions happens once, outside the measurement.
- Each library is called through the high-level API from its documentation.
- Allocations are managed memory only; memory allocated by a native Lua or Luau is not counted.

The run also writes `*-report-time.svg` and `*-report-allocations.svg` to `BenchmarkDotNet.Artifacts/results`.

NuLua's Luau backend ships a native library with the same file name as Darp.Luau's, so its benchmarks are a separate project that BenchmarkDotNet builds into its own executable.

### Publishing

The `Benchmark libraries` workflow is started by hand and commits the charts and the full table to the `benchmark-results` branch. The repository README shows the time chart from there.
