using BenchmarkDotNet.Attributes;

namespace Darp.Luau.Benchmarks.Libraries;

[BenchmarkCategory(Library.DarpLuau)]
public class DarpLuauBenchmarks : IDisposable
{
    private readonly LuauState _state = new();
    private readonly LuauFunction _managedAdd;
    private readonly LuauFunction _managedAddCompleted;
    private readonly LuauFunction _managedAddYielding;
    private readonly LuauFunction _add;
    private readonly LuauFunction _callManagedAdd;
    private readonly LuauFunction _callManagedAddAsync;
    private readonly LuauFunction _fib;
    private readonly LuauTable _table;

    public DarpLuauBenchmarks()
    {
        _managedAdd = _state.CreateFunction((double a, double b) => a + b);
        _state.Globals.Set("managed_add", _managedAdd);
        _state.Load(Scenario.Script).Execute();
        _state.Load(Scenario.AsyncScript).Execute();

        _managedAddCompleted = _state.CreateFunctionBuilder(args =>
        {
            if (!args.TryReadNumber(1, out double a, out string? error))
                return LuauReturn.Error(error);
            if (!args.TryReadNumber(2, out double b, out error))
                return LuauReturn.Error(error);
            return LuauReturn.Await(ValueTask.FromResult(LuauReturn.Ok(a + b)));
        });
        _managedAddYielding = _state.CreateFunctionBuilder(args =>
        {
            if (!args.TryReadNumber(1, out double a, out string? error))
                return LuauReturn.Error(error);
            if (!args.TryReadNumber(2, out double b, out error))
                return LuauReturn.Error(error);
            return LuauReturn.Await(AddYielding(a, b));
        });

        _add = _state.Globals.GetLuauFunction("add");
        _callManagedAdd = _state.Globals.GetLuauFunction("call_managed_add");
        _callManagedAddAsync = _state.Globals.GetLuauFunction("call_managed_add_async");
        _fib = _state.Globals.GetLuauFunction("fib");
        _table = _state.CreateTable();
    }

    [Benchmark(Description = Scenario.CreateState)]
    public void CreateState()
    {
        using var state = new LuauState();
    }

    [Benchmark(Description = Scenario.CallLuaFunction)]
    public double CallLuaFunction() => _add.Invoke<double>(Scenario.AddLeft, Scenario.AddRight);

    [Benchmark(Description = Scenario.CallManagedFunction, OperationsPerInvoke = Scenario.ManagedCallsPerInvoke)]
    public double CallManagedFunction() => _callManagedAdd.Invoke<double>(Scenario.ManagedCalls);

    [Benchmark(Description = Scenario.CallLuaFunctionAsync)]
    public ValueTask<double> CallLuaFunctionAsync() => _add.InvokeAsync<double>(Scenario.AddLeft, Scenario.AddRight);

    [Benchmark(
        Description = Scenario.CallManagedFunctionAsyncCompleted,
        OperationsPerInvoke = Scenario.ManagedCallsPerInvoke
    )]
    public ValueTask<double> CallManagedFunctionAsyncCompleted() =>
        _callManagedAddAsync.InvokeAsync<double>(Scenario.ManagedCalls, _managedAddCompleted);

    [Benchmark(
        Description = Scenario.CallManagedFunctionAsyncYielding,
        OperationsPerInvoke = Scenario.ManagedCallsPerInvoke
    )]
    public ValueTask<double> CallManagedFunctionAsyncYielding() =>
        _callManagedAddAsync.InvokeAsync<double>(Scenario.ManagedCalls, _managedAddYielding);

    private static async ValueTask<LuauReturn> AddYielding(double a, double b)
    {
        await Task.Yield();
        return LuauReturn.Ok(a + b);
    }

    [Benchmark(Description = Scenario.TableSetAndGet)]
    public double TableSetAndGet()
    {
        _table.Set("key", Scenario.TableValue);
        return _table.GetNumber("key");
    }

    [Benchmark(Description = Scenario.RunScript)]
    public double RunScript() => _fib.Invoke<double>(Scenario.FibonacciInput);

    [GlobalCleanup]
    public void Dispose()
    {
        _table.Dispose();
        _fib.Dispose();
        _callManagedAdd.Dispose();
        _callManagedAddAsync.Dispose();
        _add.Dispose();
        _managedAdd.Dispose();
        _managedAddCompleted.Dispose();
        _managedAddYielding.Dispose();
        _state.Dispose();
        GC.SuppressFinalize(this);
    }
}
