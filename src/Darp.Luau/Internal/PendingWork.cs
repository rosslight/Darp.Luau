namespace Darp.Luau.Internal;

/// <summary> The work a pending callback result waits for, and how its outcome becomes a <see cref="LuauReturn"/>. </summary>
internal readonly struct PendingWork
{
    // A Task<LuauReturn>, or a Conversion for work that produces something else.
    private readonly object? _work;

    public PendingWork(Task<LuauReturn> work) => _work = work;

    private PendingWork(Conversion conversion) => _work = conversion;

    /// <summary> Work without a result, which completes with <see cref="LuauReturn.Ok()"/>. </summary>
    public static PendingWork Create(Task work) => new(new Completion(work));

    /// <summary> Work whose result <paramref name="complete"/> converts once it is available. </summary>
    public static PendingWork Create<T>(Task<T> work, Func<T, LuauReturn> complete) =>
        new(new Conversion<T>(work, complete));

    public bool IsNone => _work is null;

    /// <summary> The work to await before <see cref="GetResult"/>. </summary>
    public Task Task =>
        _work switch
        {
            Task task => task,
            Conversion conversion => conversion.Work,
            _ => throw new InvalidOperationException("There is no pending work."),
        };

    /// <summary> The result of the completed work. Runs the conversion, so call it where the state may be used. </summary>
    public LuauReturn GetResult() =>
        _work switch
        {
            Task<LuauReturn> task => task.GetAwaiter().GetResult(),
            Conversion conversion => conversion.GetResult(),
            _ => throw new InvalidOperationException("There is no pending work."),
        };

    private abstract class Conversion
    {
        public abstract Task Work { get; }

        public abstract LuauReturn GetResult();
    }

    private sealed class Completion(Task work) : Conversion
    {
        public override Task Work { get; } = work;

        public override LuauReturn GetResult()
        {
            Work.GetAwaiter().GetResult();
            return LuauReturn.Ok();
        }
    }

    private sealed class Conversion<T>(Task<T> work, Func<T, LuauReturn> complete) : Conversion
    {
        private readonly Task<T> _work = work;
        private readonly Func<T, LuauReturn> _complete = complete;

        public override Task Work => _work;

        public override LuauReturn GetResult() => _complete(_work.GetAwaiter().GetResult());
    }
}
