using BenchmarkDotNet.Attributes;

namespace Darp.Luau.Benchmarks;

[LuauUserdata("Counter")]
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
    private readonly LuauFunction _identity;
    private readonly LuauFunction _newCounter;
    private readonly LuauFunction _createInLuau;

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
        _identity = _state.Load("return ...").ToFunction();
        _newCounter = _state.CreateFunction(() => new Counter());
        _state.Globals.Set("newCounter", _newCounter);
        _createInLuau = _state
            .Load(
                """
                local n = ...
                for i = 1, n do
                    local counter = newCounter()
                end
                """
            )
            .ToFunction();
    }

    /// <summary> One callback that returns a new managed userdata, as a slice or a node of a dissector does. </summary>
    [Benchmark(OperationsPerInvoke = CallsPerInvoke)]
    public void ReturnUserdata() => _createInLuau.Invoke(CallsPerInvoke);

    /// <summary> One managed userdata passed to a Luau function, which already has a userdata for it. </summary>
    [Benchmark]
    public void PassUserdata() => _identity.Invoke(IntoLuau.FromUserdata(_counter));

    /// <summary> One method call from Luau on managed userdata. </summary>
    [Benchmark(OperationsPerInvoke = CallsPerInvoke)]
    public double CallMethod() => _callMethod.Invoke<double>(IntoLuau.FromUserdata(_counter), CallsPerInvoke);

    /// <summary> One property read from Luau on managed userdata. </summary>
    [Benchmark(OperationsPerInvoke = CallsPerInvoke)]
    public double ReadProperty() => _readProperty.Invoke<double>(IntoLuau.FromUserdata(_counter), CallsPerInvoke);

    /// <summary> One managed object that reaches Luau for the first time. </summary>
    [Benchmark(OperationsPerInvoke = CallsPerInvoke)]
    public void CreateUserdata()
    {
        for (int i = 0; i < CallsPerInvoke; i++)
        {
            using LuauUserdata userdata = _state.GetOrCreateUserdata(new Counter());
        }
    }

    [GlobalCleanup]
    public void Dispose()
    {
        _createInLuau.Dispose();
        _newCounter.Dispose();
        _identity.Dispose();
        _readProperty.Dispose();
        _callMethod.Dispose();
        _state.Dispose();
        GC.SuppressFinalize(this);
    }
}
