using BenchmarkDotNet.Attributes;

namespace Darp.Luau.Benchmarks.Libraries;

[BenchmarkCategory(Library.DarpLuau)]
public class DarpLuauBenchmarks : IDisposable
{
    private readonly LuauState _state = new();
    private readonly LuauFunction _managedAdd;
    private readonly LuauFunction _add;
    private readonly LuauFunction _callManagedAdd;
    private readonly LuauFunction _fib;
    private readonly LuauTable _table;

    public DarpLuauBenchmarks()
    {
        _managedAdd = _state.CreateFunction((double a, double b) => a + b);
        _state.Globals.Set("managed_add", _managedAdd);
        _state.Load(Scenario.Script).Execute();

        _add = _state.Globals.GetLuauFunction("add");
        _callManagedAdd = _state.Globals.GetLuauFunction("call_managed_add");
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
        _add.Dispose();
        _managedAdd.Dispose();
        _state.Dispose();
        GC.SuppressFinalize(this);
    }
}
