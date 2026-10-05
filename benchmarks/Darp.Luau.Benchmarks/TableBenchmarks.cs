using BenchmarkDotNet.Attributes;

namespace Darp.Luau.Benchmarks;

public class TableBenchmarks : IDisposable
{
    private readonly LuauState _state = new();
    private readonly LuauTable _table;

    public TableBenchmarks()
    {
        _table = _state.CreateTable();
        _table.Set("key", 42);
    }

    [Benchmark]
    public void Create()
    {
        using LuauTable table = _state.CreateTable();
    }

    [Benchmark]
    public void SetNumber() => _table.Set("key", 42);

    [Benchmark]
    public double GetNumber() => _table.GetNumber("key");

    [GlobalCleanup]
    public void Dispose()
    {
        _table.Dispose();
        _state.Dispose();
        GC.SuppressFinalize(this);
    }
}
