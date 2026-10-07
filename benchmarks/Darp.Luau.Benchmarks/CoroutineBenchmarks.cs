using BenchmarkDotNet.Attributes;

namespace Darp.Luau.Benchmarks;

public class CoroutineBenchmarks : IDisposable
{
    private readonly LuauState _state = new();
    private readonly LuauFunction _luauAdd;
    private readonly LuauFunction _awaitOnce;
    private readonly LuauFunction _callAwaitOnce;
    private readonly LuauFunction _yieldNumbers;
    private LuauCoroutine _coroutine;

    public CoroutineBenchmarks()
    {
        _luauAdd = _state
            .Load(
                """
                local a, b = ...
                return a + b
                """
            )
            .ToFunction();

        _awaitOnce = _state.CreateFunctionBuilder(static _ => LuauReturn.Await(YieldOnce()));
        _state.Globals.Set("await_once", _awaitOnce);
        _callAwaitOnce = _state.Load("return await_once()").ToFunction();

        _yieldNumbers = _state
            .Load(
                """
                local i = 0
                while true do
                    i = i + 1
                    coroutine.yield(i)
                end
                """
            )
            .ToFunction();
    }

    private static async ValueTask<LuauReturn> YieldOnce()
    {
        await Task.Yield();
        return LuauReturn.Ok(1);
    }

    [GlobalSetup(Target = nameof(ResumeYieldingCoroutine))]
    public void CreateCoroutine() => _coroutine = _state.CreateCoroutine(_yieldNumbers);

    /// <summary> One call from C# into a Luau function through the async path, without any await. </summary>
    [Benchmark]
    public ValueTask<double> InvokeAsyncLuauFunction() => _luauAdd.InvokeAsync<double>([1, 2], CancellationToken.None);

    /// <summary> One managed callback that suspends the script once and resumes it with its result. </summary>
    [Benchmark]
    public ValueTask<double> InvokeAsyncWithOneAwait() =>
        _callAwaitOnce.InvokeAsync<double>([], CancellationToken.None);

    /// <summary> One resume of a coroutine that yields a number. </summary>
    [Benchmark]
    public double ResumeYieldingCoroutine() => _coroutine.Resume<double>();

    [GlobalCleanup]
    public void Dispose()
    {
        _coroutine.Dispose();
        _yieldNumbers.Dispose();
        _callAwaitOnce.Dispose();
        _awaitOnce.Dispose();
        _luauAdd.Dispose();
        _state.Dispose();
        GC.SuppressFinalize(this);
    }
}
