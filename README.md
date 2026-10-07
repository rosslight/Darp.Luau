# Benchmark results

[Benchmarks](https://github.com/rosslight/Darp.Luau/tree/main/benchmarks) at commit [`2b4e573`](https://github.com/rosslight/Darp.Luau/commit/2b4e57355d34caa6a1259a6ef177f720d36fe3dc), measured in [this run](https://github.com/rosslight/Darp.Luau/actions/runs/37635836690) on a GitHub-hosted runner.

![Mean time and managed memory allocated per operation](libraries.svg)

## All measurements

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 3.09GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3


```
| Method                                           | Categories | Mean            | Error        | StdDev       | Gen0   | Gen1   | Allocated |
|------------------------------------------------- |----------- |----------------:|-------------:|-------------:|-------:|-------:|----------:|
| &#39;Create and dispose a state&#39;                     | Darp.Luau  |    27,310.98 ns |   119.695 ns |   111.963 ns | 0.0916 | 0.0305 |    1912 B |
| &#39;Call a Lua function from C#&#39;                    | Darp.Luau  |       178.47 ns |     0.345 ns |     0.306 ns |      - |      - |         - |
| &#39;Call a C# function from Lua&#39;                    | Darp.Luau  |       192.87 ns |     0.238 ns |     0.222 ns |      - |      - |         - |
| &#39;Call a Lua function from C# asynchronously&#39;     | Darp.Luau  |       690.86 ns |     2.230 ns |     1.977 ns | 0.0105 |      - |     176 B |
| &#39;Call an async C# function from Lua (completed)&#39; | Darp.Luau  |       303.06 ns |     0.638 ns |     0.597 ns |      - |      - |         - |
| &#39;Call an async C# function from Lua (yielding)&#39;  | Darp.Luau  |       830.44 ns |    13.937 ns |    13.036 ns | 0.0508 |      - |     857 B |
| &#39;Set and get a table field&#39;                      | Darp.Luau  |       173.80 ns |     0.144 ns |     0.135 ns |      - |      - |         - |
| &#39;Run fib(20) in Lua&#39;                             | Darp.Luau  |   442,155.97 ns | 1,076.465 ns |   954.258 ns |      - |      - |         - |
| &#39;Create and dispose a state&#39;                     | Lua-CSharp |     6,154.29 ns |    70.315 ns |    62.333 ns | 1.3351 | 0.0916 |   22344 B |
| &#39;Call a Lua function from C#&#39;                    | Lua-CSharp |       145.56 ns |     0.245 ns |     0.191 ns | 0.0029 |      - |      48 B |
| &#39;Call a C# function from Lua&#39;                    | Lua-CSharp |       100.02 ns |     0.223 ns |     0.186 ns |      - |      - |         - |
| &#39;Call a Lua function from C# asynchronously&#39;     | Lua-CSharp |       151.63 ns |     0.252 ns |     0.210 ns | 0.0029 |      - |      48 B |
| &#39;Call an async C# function from Lua (completed)&#39; | Lua-CSharp |        78.02 ns |     0.102 ns |     0.095 ns |      - |      - |         - |
| &#39;Call an async C# function from Lua (yielding)&#39;  | Lua-CSharp |       608.47 ns |    10.862 ns |    10.160 ns | 0.0078 |      - |     136 B |
| &#39;Set and get a table field&#39;                      | Lua-CSharp |        24.29 ns |     0.061 ns |     0.054 ns |      - |      - |         - |
| &#39;Run fib(20) in Lua&#39;                             | Lua-CSharp | 2,335,525.27 ns | 8,680.103 ns | 6,776.854 ns |      - |      - |      48 B |
| &#39;Create and dispose a state&#39;                     | NLua       |    82,166.18 ns |   404.484 ns |   358.564 ns | 0.3662 |      - |    6608 B |
| &#39;Call a Lua function from C#&#39;                    | NLua       |       171.82 ns |     0.535 ns |     0.500 ns | 0.0114 |      - |     192 B |
| &#39;Call a C# function from Lua&#39;                    | NLua       |       707.60 ns |     2.666 ns |     2.364 ns | 0.0273 |      - |     504 B |
| &#39;Set and get a table field&#39;                      | NLua       |       153.37 ns |     0.509 ns |     0.425 ns | 0.0067 |      - |     112 B |
| &#39;Run fib(20) in Lua&#39;                             | NLua       |   520,730.59 ns | 1,435.614 ns | 1,272.635 ns |      - |      - |     168 B |
| &#39;Create and dispose a state&#39;                     | NuLua      |    26,267.39 ns |    30.563 ns |    27.093 ns | 0.0305 |      - |     520 B |
| &#39;Call a Lua function from C#&#39;                    | NuLua      |       117.81 ns |     0.214 ns |     0.190 ns | 0.0033 |      - |      56 B |
| &#39;Call a C# function from Lua&#39;                    | NuLua      |       256.25 ns |     0.211 ns |     0.176 ns |      - |      - |         - |
| &#39;Call a Lua function from C# asynchronously&#39;     | NuLua      |     1,508.86 ns |    20.443 ns |    19.122 ns | 0.0381 | 0.0362 |     664 B |
| &#39;Call an async C# function from Lua (completed)&#39; | NuLua      |       257.50 ns |     0.720 ns |     0.601 ns | 0.0049 | 0.0010 |      89 B |
| &#39;Call an async C# function from Lua (yielding)&#39;  | NuLua      |              NA |           NA |           NA |     NA |     NA |        NA |
| &#39;Set and get a table field&#39;                      | NuLua      |       414.48 ns |     0.568 ns |     0.444 ns |      - |      - |         - |
| &#39;Run fib(20) in Lua&#39;                             | NuLua      |   573,224.98 ns |   849.983 ns |   753.487 ns |      - |      - |      56 B |

Benchmarks with issues:
  NuLuaBenchmarks.'Call an async C# function from Lua (yielding)': DefaultJob
