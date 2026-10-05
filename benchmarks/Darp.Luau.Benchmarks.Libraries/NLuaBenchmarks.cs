using System.Globalization;
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

    [Benchmark(Description = Scenario.CreateState)]
    public void CreateState()
    {
        using var state = new NLua.Lua();
    }

    [Benchmark(Description = Scenario.CallLuaFunction)]
    public double CallLuaFunction() => ToDouble(_add.Call(1, 2));

    [Benchmark(Description = Scenario.CallManagedFunction, OperationsPerInvoke = Scenario.ManagedCallsPerInvoke)]
    public double CallManagedFunction() => ToDouble(_callManagedAdd.Call(Scenario.ManagedCallsPerInvoke));

    [Benchmark(Description = Scenario.TableSetAndGet)]
    public double TableSetAndGet()
    {
        _table["key"] = 42;
        return Convert.ToDouble(_table["key"], CultureInfo.InvariantCulture);
    }

    [Benchmark(Description = Scenario.RunScript)]
    public double RunScript() => ToDouble(_fib.Call(Scenario.FibonacciInput));

    /// <summary> Lua 5.4 returns integers as <see cref="long"/> and floats as <see cref="double"/>. </summary>
    private static double ToDouble(object[] results) => Convert.ToDouble(results[0], CultureInfo.InvariantCulture);

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
