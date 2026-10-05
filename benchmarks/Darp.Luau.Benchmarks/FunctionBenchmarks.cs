using BenchmarkDotNet.Attributes;

namespace Darp.Luau.Benchmarks;

public class FunctionBenchmarks : IDisposable
{
    private const int CallsPerInvoke = 1000;

    private readonly LuauState _state = new();
    private readonly LuauFunction _managedAdd;
    private readonly LuauFunction _luauAdd;
    private readonly LuauFunction _callManagedAdd;

    public FunctionBenchmarks()
    {
        _managedAdd = _state.CreateFunction((int a, int b) => a + b);
        _state.Globals.Set("add", _managedAdd);

        _luauAdd = _state
            .Load(
                """
                local a, b = ...
                return a + b
                """
            )
            .ToFunction();
        _callManagedAdd = _state
            .Load(
                """
                local n = ...
                local sum = 0
                for i = 1, n do
                    sum = sum + add(i, i)
                end
                return sum
                """
            )
            .ToFunction();
    }

    /// <summary> One call from C# into a Luau function. </summary>
    [Benchmark]
    public double InvokeLuauFunction() => _luauAdd.Invoke<double>(1, 2);

    /// <summary> One call from Luau into a managed callback. </summary>
    [Benchmark(OperationsPerInvoke = CallsPerInvoke)]
    public double CallManagedCallback() => _callManagedAdd.Invoke<double>(CallsPerInvoke);

    [GlobalCleanup]
    public void Dispose()
    {
        _callManagedAdd.Dispose();
        _luauAdd.Dispose();
        _managedAdd.Dispose();
        _state.Dispose();
        GC.SuppressFinalize(this);
    }
}
