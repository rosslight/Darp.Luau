using BenchmarkDotNet.Attributes;
using Lua;
using Lua.Standard;

namespace Darp.Luau.Benchmarks.Libraries;

/// <summary> Lua-CSharp only has an asynchronous API, so its benchmarks are asynchronous. </summary>
[BenchmarkCategory(Library.LuaCSharp)]
public class LuaCSharpBenchmarks
{
    private readonly LuaState _state = LuaState.Create();
    private readonly LuaValue _add;
    private readonly LuaValue _callManagedAdd;
    private readonly LuaValue _fib;
    private readonly LuaTable _table = new();

    public LuaCSharpBenchmarks()
    {
        _state.OpenStandardLibraries();
        _state.Environment["managed_add"] = new LuaFunction(
            (context, _) =>
            {
                double a = context.GetArgument<double>(0);
                double b = context.GetArgument<double>(1);
                return new ValueTask<int>(context.Return(a + b));
            }
        );
        _state.DoStringAsync(Scenario.Script).AsTask().GetAwaiter().GetResult();

        _add = _state.Environment["add"];
        _callManagedAdd = _state.Environment["call_managed_add"];
        _fib = _state.Environment["fib"];
    }

    [Benchmark(Description = Scenario.CreateState)]
    public LuaState CreateState()
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        return state;
    }

    [Benchmark(Description = Scenario.CallLuaFunction)]
    public async ValueTask<double> CallLuaFunction() => (await _state.CallAsync(_add, [1, 2]))[0].Read<double>();

    [Benchmark(Description = Scenario.CallManagedFunction, OperationsPerInvoke = Scenario.ManagedCallsPerInvoke)]
    public async ValueTask<double> CallManagedFunction() =>
        (await _state.CallAsync(_callManagedAdd, [Scenario.ManagedCallsPerInvoke]))[0].Read<double>();

    [Benchmark(Description = Scenario.TableSetAndGet)]
    public double TableSetAndGet()
    {
        _table["key"] = 42;
        return _table["key"].Read<double>();
    }

    [Benchmark(Description = Scenario.RunScript)]
    public async ValueTask<double> RunScript() =>
        (await _state.CallAsync(_fib, [Scenario.FibonacciInput]))[0].Read<double>();
}
