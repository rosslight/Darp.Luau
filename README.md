# Benchmark results

[Benchmarks](https://github.com/rosslight/Darp.Luau/tree/main/benchmarks) at commit [`4cff85d`](https://github.com/rosslight/Darp.Luau/commit/4cff85dc3a042b42e59ae16b15fd8530683e2d0c), measured in [this run](https://github.com/rosslight/Darp.Luau/actions/runs/37641801159) on a GitHub-hosted runner.

![Mean time and managed memory allocated per operation](libraries.svg)

## All measurements

```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 3.24GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3


```
| Method                                           | Categories | Mean            | Error        | StdDev       | Gen0   | Gen1   | Allocated |
|------------------------------------------------- |----------- |----------------:|-------------:|-------------:|-------:|-------:|----------:|
| &#39;Create and dispose a state&#39;                     | Darp.Luau  |    27,815.46 ns |    89.758 ns |    79.568 ns | 0.0916 | 0.0305 |    1912 B |
| &#39;Call a Lua function from C#&#39;                    | Darp.Luau  |       179.63 ns |     0.380 ns |     0.337 ns |      - |      - |         - |
| &#39;Call a C# function from Lua&#39;                    | Darp.Luau  |       188.51 ns |     0.175 ns |     0.155 ns |      - |      - |         - |
| &#39;Call a Lua function from C# asynchronously&#39;     | Darp.Luau  |       700.96 ns |     2.223 ns |     2.080 ns | 0.0105 |      - |     176 B |
| &#39;Call an async C# function from Lua (completed)&#39; | Darp.Luau  |       307.29 ns |     0.420 ns |     0.350 ns |      - |      - |         - |
| &#39;Call an async C# function from Lua (yielding)&#39;  | Darp.Luau  |       877.25 ns |    16.713 ns |    16.414 ns | 0.0508 |      - |     857 B |
| &#39;Set and get a table field&#39;                      | Darp.Luau  |       177.45 ns |     0.653 ns |     0.611 ns |      - |      - |         - |
| &#39;Run fib(20) in Lua&#39;                             | Darp.Luau  |   444,228.51 ns | 1,234.360 ns | 1,030.746 ns |      - |      - |         - |
| &#39;Create and dispose a state&#39;                     | Lua-CSharp |     6,419.00 ns |    80.030 ns |    74.860 ns | 1.3351 | 0.0916 |   22344 B |
| &#39;Call a Lua function from C#&#39;                    | Lua-CSharp |       151.60 ns |     1.127 ns |     0.941 ns | 0.0029 |      - |      48 B |
| &#39;Call a C# function from Lua&#39;                    | Lua-CSharp |        99.50 ns |     1.060 ns |     0.992 ns |      - |      - |         - |
| &#39;Call a Lua function from C# asynchronously&#39;     | Lua-CSharp |       155.66 ns |     0.195 ns |     0.183 ns | 0.0029 |      - |      48 B |
| &#39;Call an async C# function from Lua (completed)&#39; | Lua-CSharp |        79.57 ns |     0.063 ns |     0.049 ns |      - |      - |         - |
| &#39;Call an async C# function from Lua (yielding)&#39;  | Lua-CSharp |       618.43 ns |     6.585 ns |     6.160 ns | 0.0078 |      - |     136 B |
| &#39;Set and get a table field&#39;                      | Lua-CSharp |        24.80 ns |     0.019 ns |     0.015 ns |      - |      - |         - |
| &#39;Run fib(20) in Lua&#39;                             | Lua-CSharp | 2,380,195.36 ns | 1,517.113 ns | 1,344.881 ns |      - |      - |      48 B |
| &#39;Create and dispose a state&#39;                     | NLua       |    80,861.57 ns |   493.880 ns |   437.811 ns | 0.3662 |      - |    6608 B |
| &#39;Call a Lua function from C#&#39;                    | NLua       |       171.30 ns |     0.930 ns |     0.870 ns | 0.0114 |      - |     192 B |
| &#39;Call a C# function from Lua&#39;                    | NLua       |       710.61 ns |     4.439 ns |     3.707 ns | 0.0273 |      - |     504 B |
| &#39;Set and get a table field&#39;                      | NLua       |       153.79 ns |     0.547 ns |     0.485 ns | 0.0067 |      - |     112 B |
| &#39;Run fib(20) in Lua&#39;                             | NLua       |   573,634.65 ns | 5,181.104 ns | 4,846.408 ns |      - |      - |     168 B |
| &#39;Create and dispose a state&#39;                     | NuLua      |    26,427.25 ns |    74.889 ns |    62.536 ns | 0.0305 |      - |     520 B |
| &#39;Call a Lua function from C#&#39;                    | NuLua      |       132.17 ns |     0.172 ns |     0.152 ns | 0.0033 |      - |      56 B |
| &#39;Call a C# function from Lua&#39;                    | NuLua      |       260.69 ns |     0.311 ns |     0.276 ns |      - |      - |         - |
| &#39;Call a Lua function from C# asynchronously&#39;     | NuLua      |     1,532.44 ns |     8.030 ns |     7.512 ns | 0.0381 | 0.0362 |     664 B |
| &#39;Call an async C# function from Lua (completed)&#39; | NuLua      |       264.99 ns |     0.468 ns |     0.366 ns | 0.0049 | 0.0010 |      89 B |
| &#39;Set and get a table field&#39;                      | NuLua      |       420.47 ns |     0.636 ns |     0.531 ns |      - |      - |         - |
| &#39;Run fib(20) in Lua&#39;                             | NuLua      |   569,819.75 ns | 1,267.922 ns |   989.910 ns |      - |      - |      56 B |
