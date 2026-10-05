# Benchmark results

[Benchmarks](https://github.com/rosslight/Darp.Luau/tree/main/benchmarks) at commit [`d8d7b7e`](https://github.com/rosslight/Darp.Luau/commit/d8d7b7e33435c4ca8906ea4143ce600694100b02), measured in [this run](https://github.com/rosslight/Darp.Luau/actions/runs/37340041385) on a GitHub-hosted runner.

![Mean time per operation](libraries-time.svg)

![Managed memory allocated per operation](libraries-allocations.svg)

## All measurements

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 9V74 2.85GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-BVLAOW : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

IterationCount=15  WarmupCount=5  

```
| Method                        | Categories           | Mean            | Error        | StdDev       | Gen0   | Gen1   | Allocated |
|------------------------------ |--------------------- |----------------:|-------------:|-------------:|-------:|-------:|----------:|
| &#39;Create and dispose a state&#39;  | Darp.Luau            |    22,516.43 ns |   237.895 ns |   222.527 ns | 0.0916 | 0.0305 |    1768 B |
| &#39;Call a Lua function from C#&#39; | Darp.Luau            |       186.09 ns |     0.216 ns |     0.180 ns |      - |      - |         - |
| &#39;Call a C# function from Lua&#39; | Darp.Luau            |       225.31 ns |     0.178 ns |     0.139 ns |      - |      - |         - |
| &#39;Set and get a table field&#39;   | Darp.Luau            |       183.96 ns |     0.164 ns |     0.137 ns |      - |      - |         - |
| &#39;Run fib(20) in Lua&#39;          | Darp.Luau            |   471,839.43 ns | 1,342.244 ns | 1,047.936 ns |      - |      - |         - |
| &#39;Create and dispose a state&#39;  | Lua-CSharp (Lua 5.2) |     6,927.92 ns |   208.550 ns |   195.077 ns | 1.3351 | 0.0916 |   22344 B |
| &#39;Call a Lua function from C#&#39; | Lua-CSharp (Lua 5.2) |       136.78 ns |     0.381 ns |     0.337 ns | 0.0029 |      - |      48 B |
| &#39;Call a C# function from Lua&#39; | Lua-CSharp (Lua 5.2) |       101.57 ns |     0.163 ns |     0.136 ns |      - |      - |         - |
| &#39;Set and get a table field&#39;   | Lua-CSharp (Lua 5.2) |        26.20 ns |     0.037 ns |     0.032 ns |      - |      - |         - |
| &#39;Run fib(20) in Lua&#39;          | Lua-CSharp (Lua 5.2) | 2,316,930.58 ns | 8,851.771 ns | 7,846.864 ns |      - |      - |      48 B |
| &#39;Create and dispose a state&#39;  | NLua (Lua 5.4)       |    72,639.79 ns |   214.548 ns |   179.157 ns | 0.3662 |      - |    6608 B |
| &#39;Call a Lua function from C#&#39; | NLua (Lua 5.4)       |       163.11 ns |     0.681 ns |     0.637 ns | 0.0114 |      - |     192 B |
| &#39;Call a C# function from Lua&#39; | NLua (Lua 5.4)       |       623.24 ns |     5.098 ns |     4.769 ns | 0.0273 |      - |     504 B |
| &#39;Set and get a table field&#39;   | NLua (Lua 5.4)       |       148.72 ns |     0.747 ns |     0.699 ns | 0.0067 |      - |     112 B |
| &#39;Run fib(20) in Lua&#39;          | NLua (Lua 5.4)       |   638,710.31 ns | 1,329.752 ns | 1,038.183 ns |      - |      - |     168 B |
| &#39;Create and dispose a state&#39;  | NuLua (Luau)         |    25,728.68 ns |    96.310 ns |    75.193 ns | 0.0305 |      - |     520 B |
| &#39;Call a Lua function from C#&#39; | NuLua (Luau)         |       119.16 ns |     0.797 ns |     0.706 ns | 0.0033 |      - |      56 B |
| &#39;Call a C# function from Lua&#39; | NuLua (Luau)         |       157.02 ns |     0.160 ns |     0.150 ns |      - |      - |         - |
| &#39;Set and get a table field&#39;   | NuLua (Luau)         |       386.57 ns |     1.359 ns |     1.135 ns |      - |      - |         - |
| &#39;Run fib(20) in Lua&#39;          | NuLua (Luau)         |   577,316.50 ns |   647.051 ns |   505.175 ns |      - |      - |      56 B |
