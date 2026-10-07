using BenchmarkDotNet.Attributes;

namespace Darp.Luau.Benchmarks.Libraries;

[BenchmarkCategory(Library.NLua)]
public class NLuaBenchmarks : IDisposable
{
    private readonly NLua.Lua _state = new();
    private readonly NLua.LuaFunction _add;
    private readonly NLua.LuaFunction _callManagedAdd;
    private readonly NLua.LuaFunction _fib;
    private readonly NLua.LuaTable _table;

    public NLuaBenchmarks()
    {
        _state.RegisterFunction("managed_add", typeof(NLuaBenchmarks).GetMethod(nameof(ManagedAdd)));
        _state.DoString(Scenario.Script);

        _add = _state.GetFunction("add");
        _callManagedAdd = _state.GetFunction("call_managed_add");
        _fib = _state.GetFunction("fib");
        _state.NewTable("benchmark_table");
        _table = _state.GetTable("benchmark_table");
    }

    public static double ManagedAdd(double a, double b) => a + b;

    [Benchmark(Description = Scenario.CreateState, OperationsPerInvoke = 1)]
    public void CreateState()
    {
        using var state = new NLua.Lua();
    }

    [Benchmark(Description = Scenario.CallLuaFunction, OperationsPerInvoke = 1)]
    public double CallLuaFunction() => (double)_add.Call(Scenario.AddLeft, Scenario.AddRight)[0];

    [Benchmark(Description = Scenario.CallManagedFunction, OperationsPerInvoke = Scenario.ManagedCallsPerInvoke)]
    public double CallManagedFunction() => (double)_callManagedAdd.Call(Scenario.ManagedCalls)[0];

    [Benchmark(Description = Scenario.TableSetAndGet, OperationsPerInvoke = 1)]
    public double TableSetAndGet()
    {
        // The object indexer reads a plain key. The string indexer would parse "key" as a dotted path on every access.
        _table[(object)"key"] = Scenario.TableValue;
        return (double)_table[(object)"key"];
    }

    [Benchmark(Description = Scenario.RunScript, OperationsPerInvoke = 1)]
    public double RunScript() => (double)_fib.Call(Scenario.FibonacciInput)[0];

    [GlobalCleanup]
    public void Dispose()
    {
        _table.Dispose();
        _fib.Dispose();
        _callManagedAdd.Dispose();
        _add.Dispose();
        _state.Dispose();
        GC.SuppressFinalize(this);
    }
}
