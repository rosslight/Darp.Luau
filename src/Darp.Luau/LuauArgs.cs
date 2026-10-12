using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Internal;
using Darp.Luau.Native;
using Darp.Luau.Utils;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau;

/// <summary>
/// The arguments of a call from Luau into a managed callback, such as a <see cref="LuauCallback"/>.
/// </summary>
/// <remarks>
/// Read an argument through the indexer: <c>args[1].Get&lt;int&gt;()</c>. The arguments are only valid while the
/// callback runs.
/// </remarks>
public readonly unsafe ref struct LuauArgs
{
    private readonly LuauState? _state;
    private readonly lua_State* _luaState;
    private readonly int _firstStackIndex;

    /// <summary> Gets the number of arguments the caller passed. </summary>
    public int Count { get; }

    /// <summary> Gets the argument at <paramref name="index"/>. </summary>
    /// <param name="index">1-based index of the argument.</param>
    /// <returns>The argument. An index past <see cref="Count"/> gives an argument that was not passed, which is nil.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="index"/> is less than 1.</exception>
    public LuauValueView this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(index, 1);
            // An argument that was not passed has no stack slot: the slot after the last argument can hold a temporary.
            int stackIndex = index > Count ? 0 : _firstStackIndex + index - 1;
            return new LuauValueView(_state, _luaState, stackIndex, position: index);
        }
    }

    /// <summary> Gets the state that runs the callback, for example to create a table the callback returns. </summary>
    public LuauState State
    {
        get
        {
            _state.ThrowIfDisposed();
            return _state;
        }
    }

    /// <summary>
    /// Gets the cancellation token of the async host call that runs this callback, such as
    /// <c>InvokeAsync</c>, <c>ExecuteAsync</c> or <c>ResumeAsync</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="CancellationToken.None"/> outside an async host call. Pass it to the work a callback awaits through
    /// its <see cref="LuauAwaiter"/> and to the async host calls it makes: cancelling it stops the script, but not
    /// the callback, its work, or other host calls.
    /// </remarks>
    public CancellationToken CancellationToken
    {
        get
        {
            _state.ThrowIfDisposed();
            return _state.AsyncDrives.GetCancellationToken(_luaState);
        }
    }

    /// <summary> Asks whether this callback may finish later, and gets the means to do so. </summary>
    /// <param name="awaiter">Turns work into a pending result when the callback may suspend its coroutine.</param>
    /// <param name="error">Why the callback cannot suspend its coroutine here.</param>
    /// <returns><c>true</c> when the callback may suspend its coroutine; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// A callback can only suspend a coroutine that an async host call drives (<c>InvokeAsync</c>,
    /// <c>ExecuteAsync</c> or <c>ResumeAsync</c>), and only where Luau can yield: not inside a metamethod, a
    /// <c>table.sort</c> comparator, or a synchronous <c>Invoke</c> of another callback. Ask before you start the
    /// work, and return <paramref name="error"/> with <see cref="LuauReturn.Error(string)"/> when it is refused.
    /// </remarks>
    public bool TryGetAwaiter(out LuauAwaiter awaiter, [NotNullWhen(false)] out string? error)
    {
        _state.ThrowIfDisposed();
        awaiter = default;
        if (!AsyncDriveTable.IsDriving(_luaState))
        {
            error = CoroutineDriver.AwaitRejectedError;
            return false;
        }

        if (lua_isyieldable(_luaState) == 0)
        {
            error = CoroutineDriver.AwaitNotYieldableError;
            return false;
        }

        awaiter = new LuauAwaiter(isGranted: true);
        error = null;
        return true;
    }

    /// <summary>
    /// Initializes a new argument view over a specific call-frame window.
    /// </summary>
    /// <param name="state">Owning Lua state.</param>
    /// <param name="argumentCount">Number of arguments available in this call frame.</param>
    /// <param name="firstParameterStackIndex">Absolute Lua stack index of parameter <c>1</c>.</param>
    internal LuauArgs(LuauState state, int argumentCount, int firstParameterStackIndex)
        : this(state, state.L, argumentCount, firstParameterStackIndex) { }

    internal LuauArgs(LuauState state, lua_State* luaState, int argumentCount, int firstParameterStackIndex)
    {
        ArgumentNullException.ThrowIfNull(luaState);
        if (!state.OwnsThread(luaState))
            throw new InvalidOperationException("Cross-state callback argument usage is not allowed.");

        _state = state;
        _luaState = luaState;
        Count = argumentCount;
        _firstStackIndex = firstParameterStackIndex;
    }

    /// <summary> Reads the result at <paramref name="index"/> of a call whose results this window holds. </summary>
    /// <param name="index">1-based index of the result.</param>
    /// <remarks> A result the call did not return is nil. </remarks>
    /// <exception cref="LuaGetException">Thrown when the result is not a <typeparamref name="T"/>.</exception>
    internal T ReadResult<T>(int index)
    {
        _state.ThrowIfDisposed();
        ReadSource source =
            index > Count ? ReadSource.Nil : ReadSource.FromStack(_state, _luaState, _firstStackIndex + index - 1);

        // A call has no second method to ask for an optional result, so nil is the null of every reference type.
        // Whether T is annotated as nullable is the caller's choice, which the method cannot see.
        if (source.IsNil && !typeof(T).IsValueType)
            return default!;

        return LuauReader.TryRead<T>(source, out var value, out ReadFailure failure)
            ? value
            : throw failure.ToException($"result #{index}");
    }
}
