using BenchmarkDotNet.Attributes;
using Lua;
using Lua.Standard;

namespace Darp.Luau.Benchmarks.Libraries;

/// <summary> Lua-CSharp only has an asynchronous API, so its benchmarks are asynchronous. </summary>
[BenchmarkCategory(Library.LuaCSharp)]
public class LuaCSharpBenchmarks : IDisposable
{
    private readonly LuaState _state = LuaState.Create();
    private readonly LuaValue _add;
    private readonly LuaValue _callManagedAdd;
    private readonly LuaValue _callManagedAddAsync;
    private readonly LuaValue _managedAddCompleted;
    private readonly LuaValue _managedAddYielding;
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
        _state.DoStringAsync(Scenario.AsyncScript).AsTask().GetAwaiter().GetResult();

        _managedAddCompleted = new LuaFunction(
            (context, _) =>
            {
                double a = context.GetArgument<double>(0);
                double b = context.GetArgument<double>(1);
                return ValueTask.FromResult(context.Return(a + b));
            }
        );
        _managedAddYielding = new LuaFunction(
            async (context, _) =>
            {
                double a = context.GetArgument<double>(0);
                double b = context.GetArgument<double>(1);
                await Task.Yield();
                return context.Return(a + b);
            }
        );

        _add = _state.Environment["add"];
        _callManagedAdd = _state.Environment["call_managed_add"];
        _callManagedAddAsync = _state.Environment["call_managed_add_async"];
        _fib = _state.Environment["fib"];
    }

    [Benchmark(Description = Scenario.CreateState)]
    public void CreateState()
    {
        using LuaState state = LuaState.Create();
        state.OpenStandardLibraries();
    }

    [Benchmark(Description = Scenario.CallLuaFunction)]
    public async ValueTask<double> CallLuaFunction() =>
        (await _state.CallAsync(_add, [Scenario.AddLeft, Scenario.AddRight]))[0].Read<double>();

    [Benchmark(Description = Scenario.CallManagedFunction, OperationsPerInvoke = Scenario.ManagedCallsPerInvoke)]
    public async ValueTask<double> CallManagedFunction() =>
        (await _state.CallAsync(_callManagedAdd, [Scenario.ManagedCalls]))[0].Read<double>();

    [Benchmark(Description = Scenario.CallLuaFunctionAsync)]
    public ValueTask<double> CallLuaFunctionAsync() => CallLuaFunction();

    [Benchmark(
        Description = Scenario.CallManagedFunctionAsyncCompleted,
        OperationsPerInvoke = Scenario.ManagedCallsPerInvoke
    )]
    public async ValueTask<double> CallManagedFunctionAsyncCompleted() =>
        (await _state.CallAsync(_callManagedAddAsync, [Scenario.ManagedCalls, _managedAddCompleted]))[0].Read<double>();

    [Benchmark(
        Description = Scenario.CallManagedFunctionAsyncYielding,
        OperationsPerInvoke = Scenario.ManagedCallsPerInvoke
    )]
    public async ValueTask<double> CallManagedFunctionAsyncYielding() =>
        (await _state.CallAsync(_callManagedAddAsync, [Scenario.ManagedCalls, _managedAddYielding]))[0].Read<double>();

    [Benchmark(Description = Scenario.TableSetAndGet)]
    public double TableSetAndGet()
    {
        _table["key"] = Scenario.TableValue;
        return _table["key"].Read<double>();
    }

    [Benchmark(Description = Scenario.RunScript)]
    public async ValueTask<double> RunScript() =>
        (await _state.CallAsync(_fib, [Scenario.FibonacciInput]))[0].Read<double>();

    [GlobalCleanup]
    public void Dispose()
    {
        _state.Dispose();
        GC.SuppressFinalize(this);
    }
}
