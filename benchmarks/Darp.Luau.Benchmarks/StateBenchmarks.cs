using BenchmarkDotNet.Attributes;

namespace Darp.Luau.Benchmarks;

public class StateBenchmarks
{
    [Benchmark]
    public void CreateAndDispose()
    {
        using var state = new LuauState();
    }
}
