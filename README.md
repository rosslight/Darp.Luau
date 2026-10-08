# Benchmark results

[Benchmarks](https://github.com/rosslight/Darp.Luau/tree/main/benchmarks) at commit [`7f9c7c3`](https://github.com/rosslight/Darp.Luau/commit/7f9c7c36dc78e205bed36d18ab22ff367d600c0c), measured in [this run](https://github.com/rosslight/Darp.Luau/actions/runs/37746939431) on a GitHub-hosted runner.

![Mean time and managed memory allocated per operation](libraries.svg)

## All measurements

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Platinum 8370C CPU 2.80GHz (Max: 3.31GHz), 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4


```
| Method                                           | Categories | Mean            | Error        | StdDev       | Gen0   | Gen1   | Allocated |
|------------------------------------------------- |----------- |----------------:|-------------:|-------------:|-------:|-------:|----------:|
| &#39;Create and dispose a state&#39;                     | Darp.Luau  |    25,342.22 ns |    57.205 ns |    47.769 ns | 0.0610 | 0.0305 |    1968 B |
| &#39;Call a Lua function from C#&#39;                    | Darp.Luau  |       152.98 ns |     0.284 ns |     0.252 ns |      - |      - |         - |
| &#39;Call a C# function from Lua&#39;                    | Darp.Luau  |       180.61 ns |     0.246 ns |     0.218 ns |      - |      - |         - |
| &#39;Call a Lua function from C# asynchronously&#39;     | Darp.Luau  |       530.40 ns |     1.122 ns |     0.995 ns |      - |      - |         - |
| &#39;Call an async C# function from Lua (completed)&#39; | Darp.Luau  |       196.53 ns |     0.116 ns |     0.091 ns |      - |      - |         - |
| &#39;Call an async C# function from Lua (yielding)&#39;  | Darp.Luau  |       565.94 ns |     5.898 ns |     4.925 ns | 0.0078 |      - |     201 B |
| &#39;Set and get a table field&#39;                      | Darp.Luau  |       157.84 ns |     0.389 ns |     0.325 ns |      - |      - |         - |
| &#39;Run fib(20) in Lua&#39;                             | Darp.Luau  |   459,264.43 ns |   403.686 ns |   337.096 ns |      - |      - |         - |
| &#39;Create and dispose a state&#39;                     | Lua-CSharp |     6,686.79 ns |    53.234 ns |    47.190 ns | 0.8850 | 0.0610 |   22344 B |
| &#39;Call a Lua function from C#&#39;                    | Lua-CSharp |       139.07 ns |     0.366 ns |     0.324 ns | 0.0019 |      - |      48 B |
| &#39;Call a C# function from Lua&#39;                    | Lua-CSharp |        90.42 ns |     0.128 ns |     0.114 ns |      - |      - |         - |
| &#39;Call a Lua function from C# asynchronously&#39;     | Lua-CSharp |       137.22 ns |     0.282 ns |     0.250 ns | 0.0019 |      - |      48 B |
| &#39;Call an async C# function from Lua (completed)&#39; | Lua-CSharp |        66.62 ns |     0.361 ns |     0.320 ns |      - |      - |         - |
| &#39;Call an async C# function from Lua (yielding)&#39;  | Lua-CSharp |       894.57 ns |     6.650 ns |     6.221 ns | 0.0049 |      - |     136 B |
| &#39;Set and get a table field&#39;                      | Lua-CSharp |        22.92 ns |     0.040 ns |     0.038 ns |      - |      - |         - |
| &#39;Run fib(20) in Lua&#39;                             | Lua-CSharp | 2,213,116.00 ns | 4,703.980 ns | 3,928.036 ns |      - |      - |      48 B |
| &#39;Create and dispose a state&#39;                     | NLua       |    78,762.02 ns | 1,525.477 ns | 1,566.552 ns | 0.2441 |      - |    6608 B |
| &#39;Call a Lua function from C#&#39;                    | NLua       |       178.26 ns |     2.511 ns |     2.349 ns | 0.0076 |      - |     192 B |
| &#39;Call a C# function from Lua&#39;                    | NLua       |       781.31 ns |     3.289 ns |     2.746 ns | 0.0195 |      - |     504 B |
| &#39;Set and get a table field&#39;                      | NLua       |       164.06 ns |     1.074 ns |     0.952 ns | 0.0043 |      - |     112 B |
| &#39;Run fib(20) in Lua&#39;                             | NLua       |   603,488.23 ns | 1,078.734 ns |   900.792 ns |      - |      - |     168 B |
| &#39;Create and dispose a state&#39;                     | NuLua      |    25,125.75 ns |    28.511 ns |    23.808 ns |      - |      - |     520 B |
| &#39;Call a Lua function from C#&#39;                    | NuLua      |       127.90 ns |     0.224 ns |     0.187 ns | 0.0021 |      - |      56 B |
| &#39;Call a C# function from Lua&#39;                    | NuLua      |       117.53 ns |     0.080 ns |     0.067 ns |      - |      - |         - |
| &#39;Call a Lua function from C# asynchronously&#39;     | NuLua      |     1,575.77 ns |    29.931 ns |    30.737 ns | 0.0248 | 0.0229 |     664 B |
| &#39;Call an async C# function from Lua (completed)&#39; | NuLua      |       137.14 ns |     0.378 ns |     0.335 ns | 0.0034 | 0.0010 |      89 B |
| &#39;Set and get a table field&#39;                      | NuLua      |       413.57 ns |     0.343 ns |     0.286 ns |      - |      - |         - |
| &#39;Run fib(20) in Lua&#39;                             | NuLua      |   564,598.41 ns |   323.855 ns |   287.089 ns |      - |      - |      56 B |
