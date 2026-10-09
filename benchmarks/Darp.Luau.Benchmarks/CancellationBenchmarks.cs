using BenchmarkDotNet.Attributes;

namespace Darp.Luau.Benchmarks;

/// <summary> What a script pays for a host call whose token can stop it. </summary>
public class CancellationBenchmarks : IDisposable
{
    private const int Iterations = 1000;

    private readonly LuauState _state = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly LuauFunction _managedAdd;
    private readonly LuauFunction _loop;
    private readonly LuauFunction _callManagedAdd;

    public CancellationBenchmarks()
    {
        _managedAdd = _state.CreateFunction((int a, int b) => a + b);
        _state.Globals.Set("add", _managedAdd);

        _loop = _state
            .Load(
                """
                local n = ...
                local sum = 0
                for i = 1, n do
                    sum = sum + i
                end
                return sum
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

    /// <summary> One loop iteration of a script that no token can stop. </summary>
    [Benchmark(OperationsPerInvoke = Iterations)]
    public ValueTask<double> LoopWithoutToken() => _loop.InvokeAsync<double>([Iterations], CancellationToken.None);

    /// <summary> One loop iteration of a script that a token can stop. </summary>
    [Benchmark(OperationsPerInvoke = Iterations)]
    public ValueTask<double> LoopWithToken() => _loop.InvokeAsync<double>([Iterations], _cts.Token);

    /// <summary> One call into a managed callback from a script that no token can stop. </summary>
    [Benchmark(OperationsPerInvoke = Iterations)]
    public ValueTask<double> CallManagedCallbackWithoutToken() =>
        _callManagedAdd.InvokeAsync<double>([Iterations], CancellationToken.None);

    /// <summary> One call into a managed callback from a script that a token can stop. </summary>
    [Benchmark(OperationsPerInvoke = Iterations)]
    public ValueTask<double> CallManagedCallbackWithToken() =>
        _callManagedAdd.InvokeAsync<double>([Iterations], _cts.Token);

    /// <summary> One async call into a Luau function with a token that can stop it. </summary>
    [Benchmark]
    public ValueTask<double> InvokeAsyncWithToken() => _loop.InvokeAsync<double>([0], _cts.Token);

    [GlobalCleanup]
    public void Dispose()
    {
        _callManagedAdd.Dispose();
        _loop.Dispose();
        _managedAdd.Dispose();
        _state.Dispose();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
