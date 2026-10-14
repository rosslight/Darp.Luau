using BenchmarkDotNet.Attributes;

namespace Darp.Luau.Benchmarks;

public class StateBenchmarks
{
    [Benchmark]
    public void CreateAndDispose()
    {
        using var state = new LuauState();
    }

    [Benchmark]
    public void CreateSandboxedAndDispose()
    {
        using var state = new LuauState();
        state.EnableSandbox();
    }
}
