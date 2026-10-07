using BenchmarkDotNet.Attributes;
using NuLua;
using NuLua.Luau;

namespace Darp.Luau.Benchmarks.Libraries;

/// <summary> NuLua with its Luau backend, which runs the same virtual machine as Darp.Luau. </summary>
[BenchmarkCategory(Library.NuLua)]
public class NuLuaBenchmarks : IDisposable
{
    private readonly LuauState _state = LuauState.Create();
    private readonly LuaFunction _add;
    private readonly LuaFunction _callManagedAdd;
    private readonly LuaFunction _callManagedAddAsync;
    private readonly LuaFunction _managedAddCompleted;
    private readonly LuaFunction _managedAddYielding;
    private readonly LuaFunction _fib;
    private readonly LuaTable _table;

    public NuLuaBenchmarks()
    {
        _state.OpenLibraries();
        _state.RegisterFunction(
            "managed_add",
            (state, args) =>
            {
                state.Push(args[0].Read<double>() + args[1].Read<double>());
                return 1;
            }
        );
        _state.DoString(Scenario.Script);
        _state.DoString(Scenario.AsyncScript);

        _managedAddCompleted = _state.CreateFunction(
            (state, args, _) =>
            {
                state.Push(args[0].Read<double>() + args[1].Read<double>());
                return ValueTask.FromResult(1);
            }
        );
        _managedAddYielding = _state.CreateFunction(
            async (state, args, _) =>
            {
                double a = args[0].Read<double>();
                double b = args[1].Read<double>();
                await Task.Yield();
                state.Push(a + b);
                return 1;
            }
        );

        _add = _state["add"].Read<LuaFunction>();
        _callManagedAdd = _state["call_managed_add"].Read<LuaFunction>();
        _callManagedAddAsync = _state["call_managed_add_async"].Read<LuaFunction>();
        _fib = _state["fib"].Read<LuaFunction>();
        _table = _state.CreateTable();
    }

    [Benchmark(Description = Scenario.CreateState)]
    public void CreateState()
    {
        using LuauState state = LuauState.Create();
        state.OpenLibraries();
    }

    [Benchmark(Description = Scenario.CallLuaFunction)]
    public double CallLuaFunction() => _add.Invoke(Scenario.AddLeft, Scenario.AddRight)[0].Read<double>();

    [Benchmark(Description = Scenario.CallManagedFunction, OperationsPerInvoke = Scenario.ManagedCallsPerInvoke)]
    public double CallManagedFunction() => _callManagedAdd.Invoke(Scenario.ManagedCalls)[0].Read<double>();

    [Benchmark(Description = Scenario.CallLuaFunctionAsync)]
    public async ValueTask<double> CallLuaFunctionAsync() =>
        (await _add.InvokeAsync(new LuaValue[] { Scenario.AddLeft, Scenario.AddRight }))[0].Read<double>();

    [Benchmark(
        Description = Scenario.CallManagedFunctionAsyncCompleted,
        OperationsPerInvoke = Scenario.ManagedCallsPerInvoke
    )]
    public async ValueTask<double> CallManagedFunctionAsyncCompleted() =>
        (await _callManagedAddAsync.InvokeAsync(new LuaValue[] { Scenario.ManagedCalls, _managedAddCompleted }))[0]
            .Read<double>();

    [Benchmark(
        Description = Scenario.CallManagedFunctionAsyncYielding,
        OperationsPerInvoke = Scenario.ManagedCallsPerInvoke
    )]
    public async ValueTask<double> CallManagedFunctionAsyncYielding() =>
        (await _callManagedAddAsync.InvokeAsync(new LuaValue[] { Scenario.ManagedCalls, _managedAddYielding }))[0]
            .Read<double>();

    [Benchmark(Description = Scenario.TableSetAndGet)]
    public double TableSetAndGet()
    {
        _table["key"] = Scenario.TableValue;
        return _table["key"].Read<double>();
    }

    [Benchmark(Description = Scenario.RunScript)]
    public double RunScript() => _fib.Invoke(Scenario.FibonacciInput)[0].Read<double>();

    [GlobalCleanup]
    public void Dispose()
    {
        _callManagedAddAsync.Dispose();
        _managedAddCompleted.Dispose();
        _managedAddYielding.Dispose();
        _state.Dispose();
        GC.SuppressFinalize(this);
    }
}
