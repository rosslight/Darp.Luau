using BenchmarkDotNet.Attributes;

namespace Darp.Luau.Benchmarks;

/// <summary> Userdata in a host that runs one state per thread. </summary>
public class UserdataScalingBenchmarks : IDisposable
{
    private const int CallsPerState = 1000;

    private readonly List<LuauState> _states = [];
    private readonly List<LuauFunction> _functions = [];
    private LuauFunction[] _createOnState = [];

    /// <summary> The number of states, each on a thread of its own. </summary>
    [Params(1, 4)]
    public int States { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _createOnState = new LuauFunction[States];
        for (int i = 0; i < States; i++)
        {
            LuauState state = CreateState();
            _createOnState[i] = Load(
                state,
                """
                local n = ...
                for i = 1, n do
                    local counter = newCounter()
                end
                """
            );
        }
    }

    /// <summary> One new userdata returned to Luau on every state at once. Scales with the states if the time stays the same. </summary>
    [Benchmark(OperationsPerInvoke = CallsPerState)]
    public void ReturnUserdataOnEveryState() =>
        Parallel.For(
            0,
            States,
            new ParallelOptions { MaxDegreeOfParallelism = States },
            i => _createOnState[i].Invoke(CallsPerState)
        );

    private LuauState CreateState()
    {
        var state = new LuauState();
        _states.Add(state);
        LuauFunction newCounter = state.CreateFunction(() => new Counter());
        _functions.Add(newCounter);
        state.Globals.Set("newCounter", newCounter);
        return state;
    }

    private LuauFunction Load(LuauState state, string source)
    {
        LuauFunction function = state.Load(source).ToFunction();
        _functions.Add(function);
        return function;
    }

    [GlobalCleanup]
    public void Dispose()
    {
        foreach (LuauFunction function in _functions)
            function.Dispose();
        foreach (LuauState state in _states)
            state.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary> The collector of the runtime while a script keeps many userdata alive. </summary>
public class UserdataCollectionBenchmarks : IDisposable
{
    private const int LiveUserdata = 100_000;

    private readonly LuauState _state = new();
    private readonly LuauFunction _newCounter;

    public UserdataCollectionBenchmarks()
    {
        _newCounter = _state.CreateFunction(() => new Counter());
        _state.Globals.Set("newCounter", _newCounter);
        // Kept alive by a table in Luau, as the objects of a long-running script are
        using LuauFunction keep = _state
            .Load("local n = ... kept = {} for i = 1, n do kept[i] = newCounter() end")
            .ToFunction();
        keep.Invoke(LiveUserdata);
    }

    /// <summary> One collection of the youngest generation. </summary>
    [Benchmark]
    public void CollectYoungestGeneration() => GC.Collect(0, GCCollectionMode.Forced, blocking: true);

    [GlobalCleanup]
    public void Dispose()
    {
        _newCounter.Dispose();
        _state.Dispose();
        GC.SuppressFinalize(this);
    }
}
