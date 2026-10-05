# Benchmark results

Darp.Luau compared with other Lua libraries for .NET, measured by the [benchmarks](https://github.com/rosslight/Darp.Luau/tree/main/benchmarks) at commit [`dd39fec`](https://github.com/rosslight/Darp.Luau/commit/dd39fec26b28a2aaca0b62123fb955420b91a8c7) in [this run](https://github.com/rosslight/Darp.Luau/actions/runs/37330616629).

The numbers come from a shared GitHub-hosted runner. Absolute values change from run to run, so compare the libraries within one scenario.

![Mean time per operation](libraries-time.svg)

![Managed memory allocated per operation](libraries-allocations.svg)

## All measurements

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.84GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-BVLAOW : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

IterationCount=15  WarmupCount=5  

```
| Method                        | Categories           | Mean            | Error        | StdDev       | Gen0   | Gen1   | Allocated |
|------------------------------ |--------------------- |----------------:|-------------:|-------------:|-------:|-------:|----------:|
| &#39;Create a state&#39;              | Darp.Luau            |    23,052.23 ns |   125.755 ns |   105.011 ns | 0.0916 | 0.0305 |    1768 B |
| &#39;Call a Lua function from C#&#39; | Darp.Luau            |       182.53 ns |     0.346 ns |     0.307 ns |      - |      - |         - |
| &#39;Call a C# function from Lua&#39; | Darp.Luau            |       223.87 ns |     0.251 ns |     0.234 ns |      - |      - |         - |
| &#39;Set and get a table field&#39;   | Darp.Luau            |       176.40 ns |     0.159 ns |     0.124 ns |      - |      - |         - |
| &#39;Run a script: fib(20)&#39;       | Darp.Luau            |   444,774.25 ns | 2,263.521 ns | 2,006.553 ns |      - |      - |         - |
| &#39;Create a state&#39;              | Lua-CSharp (Lua 5.2) |     6,325.09 ns |    92.501 ns |    86.526 ns | 1.7090 | 0.1526 |   28592 B |
| &#39;Call a Lua function from C#&#39; | Lua-CSharp (Lua 5.2) |       145.08 ns |     0.160 ns |     0.134 ns | 0.0029 |      - |      48 B |
| &#39;Call a C# function from Lua&#39; | Lua-CSharp (Lua 5.2) |        99.07 ns |     0.060 ns |     0.051 ns |      - |      - |         - |
| &#39;Set and get a table field&#39;   | Lua-CSharp (Lua 5.2) |        24.48 ns |     0.034 ns |     0.028 ns |      - |      - |         - |
| &#39;Run a script: fib(20)&#39;       | Lua-CSharp (Lua 5.2) | 2,366,442.49 ns | 6,441.191 ns | 5,709.948 ns |      - |      - |      48 B |
| &#39;Create a state&#39;              | NLua (Lua 5.4)       |    81,435.45 ns |   366.108 ns |   324.545 ns | 0.3662 |      - |    6608 B |
| &#39;Call a Lua function from C#&#39; | NLua (Lua 5.4)       |       165.51 ns |     1.703 ns |     1.593 ns | 0.0114 |      - |     192 B |
| &#39;Call a C# function from Lua&#39; | NLua (Lua 5.4)       |       716.37 ns |     4.210 ns |     3.732 ns | 0.0273 |      - |     504 B |
| &#39;Set and get a table field&#39;   | NLua (Lua 5.4)       |       287.47 ns |     1.404 ns |     1.314 ns | 0.0181 |      - |     304 B |
| &#39;Run a script: fib(20)&#39;       | NLua (Lua 5.4)       |   559,433.43 ns | 4,691.457 ns | 4,388.392 ns |      - |      - |     168 B |
| &#39;Create a state&#39;              | NuLua (Luau)         |    26,200.51 ns |    45.607 ns |    38.084 ns | 0.0305 |      - |     520 B |
| &#39;Call a Lua function from C#&#39; | NuLua (Luau)         |       117.92 ns |     0.252 ns |     0.235 ns | 0.0033 |      - |      56 B |
| &#39;Call a C# function from Lua&#39; | NuLua (Luau)         |       255.20 ns |     0.247 ns |     0.193 ns |      - |      - |         - |
| &#39;Set and get a table field&#39;   | NuLua (Luau)         |       416.49 ns |     2.017 ns |     1.887 ns |      - |      - |         - |
| &#39;Run a script: fib(20)&#39;       | NuLua (Luau)         |   574,503.67 ns | 1,021.703 ns |   853.168 ns |      - |      - |      56 B |
