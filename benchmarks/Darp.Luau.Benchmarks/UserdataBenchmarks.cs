using BenchmarkDotNet.Attributes;

namespace Darp.Luau.Benchmarks;

[LuauUserdata]
public sealed partial class Counter
{
    [LuauMember("value")]
    public int Value { get; set; }

    [LuauMember("add")]
    public int Add(int amount) => Value + amount;
}

public class UserdataBenchmarks : IDisposable
{
    private const int CallsPerInvoke = 1000;

    private readonly LuauState _state = new();
    private readonly Counter _counter = new() { Value = 1 };
    private readonly LuauFunction _callMethod;
    private readonly LuauFunction _readProperty;

    public UserdataBenchmarks()
    {
        _callMethod = _state
            .Load(
                """
                local counter, n = ...
                local sum = 0
                for i = 1, n do
                    sum = sum + counter:add(i)
                end
                return sum
                """
            )
            .ToFunction();
        _readProperty = _state
            .Load(
                """
                local counter, n = ...
                local sum = 0
                for i = 1, n do
                    sum = sum + counter.value
                end
                return sum
                """
            )
            .ToFunction();
    }

    /// <summary> One method call from Luau on managed userdata. </summary>
    [Benchmark(OperationsPerInvoke = CallsPerInvoke)]
    public double CallMethod() => _callMethod.Invoke<double>(IntoLuau.FromUserdata(_counter), CallsPerInvoke);

    /// <summary> One property read from Luau on managed userdata. </summary>
    [Benchmark(OperationsPerInvoke = CallsPerInvoke)]
    public double ReadProperty() => _readProperty.Invoke<double>(IntoLuau.FromUserdata(_counter), CallsPerInvoke);

    [GlobalCleanup]
    public void Dispose()
    {
        _readProperty.Dispose();
        _callMethod.Dispose();
        _state.Dispose();
        GC.SuppressFinalize(this);
    }
}
