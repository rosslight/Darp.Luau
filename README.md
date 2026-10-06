# Benchmark results

[Benchmarks](https://github.com/rosslight/Darp.Luau/tree/main/benchmarks) at commit [`50cecb8`](https://github.com/rosslight/Darp.Luau/commit/50cecb82a71a9c09fb4d7ab1844ce6970e27f459), measured in [this run](https://github.com/rosslight/Darp.Luau/actions/runs/37436683966) on a GitHub-hosted runner.

![Mean time and managed memory allocated per operation](libraries.svg)

## All measurements

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 3.20GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3


```
| Method                        | Categories | Mean            | Error        | StdDev       | Gen0   | Gen1   | Allocated |
|------------------------------ |----------- |----------------:|-------------:|-------------:|-------:|-------:|----------:|
| &#39;Create and dispose a state&#39;  | Darp.Luau  |    23,618.03 ns |   126.849 ns |   112.448 ns | 0.0916 | 0.0305 |    1768 B |
| &#39;Call a Lua function from C#&#39; | Darp.Luau  |       199.24 ns |     0.158 ns |     0.123 ns |      - |      - |         - |
| &#39;Call a C# function from Lua&#39; | Darp.Luau  |       223.52 ns |     0.439 ns |     0.410 ns |      - |      - |         - |
| &#39;Set and get a table field&#39;   | Darp.Luau  |       172.75 ns |     0.240 ns |     0.213 ns |      - |      - |         - |
| &#39;Run fib(20) in Lua&#39;          | Darp.Luau  |   441,666.85 ns | 1,441.700 ns | 1,348.567 ns |      - |      - |         - |
| &#39;Create and dispose a state&#39;  | Lua-CSharp |     6,145.95 ns |    98.201 ns |    91.857 ns | 1.3351 | 0.0916 |   22344 B |
| &#39;Call a Lua function from C#&#39; | Lua-CSharp |       145.94 ns |     0.674 ns |     0.598 ns | 0.0029 |      - |      48 B |
| &#39;Call a C# function from Lua&#39; | Lua-CSharp |        99.59 ns |     0.343 ns |     0.321 ns |      - |      - |         - |
| &#39;Set and get a table field&#39;   | Lua-CSharp |        24.38 ns |     0.072 ns |     0.060 ns |      - |      - |         - |
| &#39;Run fib(20) in Lua&#39;          | Lua-CSharp | 2,363,421.67 ns | 3,706.739 ns | 3,467.286 ns |      - |      - |      48 B |
| &#39;Create and dispose a state&#39;  | NLua       |    82,576.48 ns |   430.294 ns |   381.444 ns | 0.3662 |      - |    6608 B |
| &#39;Call a Lua function from C#&#39; | NLua       |       181.19 ns |     2.454 ns |     2.296 ns | 0.0114 |      - |     192 B |
| &#39;Call a C# function from Lua&#39; | NLua       |       704.08 ns |     3.400 ns |     3.180 ns | 0.0273 |      - |     504 B |
| &#39;Set and get a table field&#39;   | NLua       |       153.72 ns |     0.750 ns |     0.664 ns | 0.0067 |      - |     112 B |
| &#39;Run fib(20) in Lua&#39;          | NLua       |   518,004.22 ns | 1,104.744 ns |   979.327 ns |      - |      - |     168 B |
| &#39;Create and dispose a state&#39;  | NuLua      |    26,247.44 ns |    60.678 ns |    53.789 ns | 0.0305 |      - |     520 B |
| &#39;Call a Lua function from C#&#39; | NuLua      |       121.09 ns |     0.291 ns |     0.258 ns | 0.0033 |      - |      56 B |
| &#39;Call a C# function from Lua&#39; | NuLua      |       256.21 ns |     0.485 ns |     0.405 ns |      - |      - |         - |
| &#39;Set and get a table field&#39;   | NuLua      |       442.37 ns |     0.343 ns |     0.287 ns |      - |      - |         - |
| &#39;Run fib(20) in Lua&#39;          | NuLua      |   573,364.28 ns |   824.169 ns |   730.605 ns |      - |      - |      56 B |
