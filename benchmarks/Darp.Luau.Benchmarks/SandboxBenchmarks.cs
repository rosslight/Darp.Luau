using BenchmarkDotNet.Attributes;

namespace Darp.Luau.Benchmarks;

/// <summary> A script that spends its time in built-in functions and a loop over a table, with and without the sandbox. </summary>
public class SandboxBenchmarks : IDisposable
{
    private const int ValuesPerInvoke = 1000;

    private readonly LuauState _state = new();
    private LuauFunction _sum;

    [Params(false, true)]
    public bool Sandboxed { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        if (Sandboxed)
            _state.EnableSandbox();
        _sum = _state
            .Load(
                $$"""
                local values = {}
                for i = 1, {{ValuesPerInvoke}} do
                    values[i] = i
                end
                return function()
                    local sum = 0
                    for _, value in ipairs(values) do
                        sum += math.floor(value * 1.5) + math.abs(-value)
                    end
                    return sum
                end
                """
            )
            .Execute<LuauFunction>();
    }

    /// <summary> One value of the loop: two built-in calls and an iteration step. </summary>
    [Benchmark(OperationsPerInvoke = ValuesPerInvoke)]
    public double LoopOverBuiltins() => _sum.Invoke<double>();

    [GlobalCleanup]
    public void Dispose()
    {
        _sum.Dispose();
        _state.Dispose();
        GC.SuppressFinalize(this);
    }
}
