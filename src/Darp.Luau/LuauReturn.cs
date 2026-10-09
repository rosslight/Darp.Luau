using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Internal;
using Darp.Luau.Native;

namespace Darp.Luau;

/// <summary>
/// Represents the return value of a managed Luau callback.
/// Use <see cref="Ok()"/> or one of the <see cref="Ok(IntoLuau)"/> overloads for successful results,
/// <see cref="Error(string)"/> to return an error, or a <see cref="LuauAwaiter"/> to finish later.
/// </summary>
/// <remarks>
/// The default value represents an error with message <c>Unknown error</c>.
/// A result is consumed once: its values are pushed to Luau, or released when Luau can no longer receive them.
/// </remarks>
[SuppressMessage(
    "Performance",
    "CA1815:Override equals and operator equals on value types",
    Justification = "A callback result owns captured values and is consumed once; value equality is meaningless."
)]
public readonly struct LuauReturn
{
    private readonly IntoLuauCopiedBuffer _buffer;
    private readonly string? _error;
    private readonly PendingWork _pending;

    /// <summary> Gets whether this callback result is successful. </summary>
    public bool IsOk { get; }

    /// <summary> Gets whether this callback result completes later. See <see cref="LuauAwaiter"/>. </summary>
    public bool IsPending => !_pending.IsNone;

    private LuauReturn(
        int valueCount,
        IntoLuau value1 = default,
        IntoLuau value2 = default,
        IntoLuau value3 = default,
        IntoLuau value4 = default
    )
    {
        // Only occupied slots are captured, so returning one value does not capture and later release four.
        _buffer = new IntoLuauCopiedBuffer(
            valueCount,
            valueCount > 0 ? value1.CaptureCopied() : default,
            valueCount > 1 ? value2.CaptureCopied() : default,
            valueCount > 2 ? value3.CaptureCopied() : default,
            valueCount > 3 ? value4.CaptureCopied() : default
        );
        IsOk = true;
    }

    private LuauReturn(string error)
    {
        IsOk = false;
        _error = error;
    }

    private LuauReturn(PendingWork pending)
    {
        IsOk = false;
        _pending = pending;
    }

    /// <summary> Creates a successful callback result with no return values. </summary>
    public static LuauReturn Ok() => new(valueCount: 0);

    /// <summary> Creates a successful callback result with one return value. </summary>
    /// <param name="value">The value to return to Luau.</param>
    public static LuauReturn Ok(IntoLuau value) => new(valueCount: 1, value);

    /// <summary> Creates a successful callback result with two return values. </summary>
    /// <param name="value1">The first value to return to Luau.</param>
    /// <param name="value2">The second value to return to Luau.</param>
    public static LuauReturn Ok(IntoLuau value1, IntoLuau value2) => new(valueCount: 2, value1, value2);

    /// <summary> Creates a successful callback result with three return values. </summary>
    /// <param name="value1">The first value to return to Luau.</param>
    /// <param name="value2">The second value to return to Luau.</param>
    /// <param name="value3">The third value to return to Luau.</param>
    public static LuauReturn Ok(IntoLuau value1, IntoLuau value2, IntoLuau value3) =>
        new(valueCount: 3, value1, value2, value3);

    /// <summary> Creates a successful callback result with four return values. </summary>
    /// <param name="value1">The first value to return to Luau.</param>
    /// <param name="value2">The second value to return to Luau.</param>
    /// <param name="value3">The third value to return to Luau.</param>
    /// <param name="value4">The fourth value to return to Luau.</param>
    public static LuauReturn Ok(IntoLuau value1, IntoLuau value2, IntoLuau value3, IntoLuau value4) =>
        new(valueCount: 4, value1, value2, value3, value4);

    /// <summary> Creates a failed callback result with an error message. </summary>
    /// <param name="error">Error message reported to the caller.</param>
    /// <remarks>When the provided text is empty or whitespace, <c>Unknown error</c> is used.</remarks>
    public static LuauReturn Error(string error) => new(error);

    /// <summary> The work a pending result waits for, if any. </summary>
    internal PendingWork Pending => _pending;

    /// <summary> Creates the pending result a <see cref="LuauAwaiter"/> hands out. </summary>
    internal static LuauReturn FromPending(PendingWork pending) => new(pending);

    /// <summary> Unwraps the result of completed pending work. A result that is pending itself is rejected. </summary>
    internal static LuauReturn FromCompleted(LuauReturn result)
    {
        if (!result.IsPending)
            return result;
        result.Release();
        return Error("nested await: the result of awaited work must not be pending itself");
    }

    /// <summary> Pushes return values when this result is successful. </summary>
    /// <param name="state">Target state that receives the return values.</param>
    /// <param name="outputCount">Number of values produced for the callback.</param>
    /// <param name="error">Receives the error message when this result is not successful.</param>
    /// <returns><c>true</c> when values are available; otherwise <c>false</c>.</returns>
    internal bool TryPushValues(LuauState state, out int outputCount, [NotNullWhen(false)] out string? error)
    {
        unsafe
        {
            return TryPushValues(state, state.L, out outputCount, out error);
        }
    }

    internal unsafe bool TryPushValues(
        LuauState state,
        lua_State* luaState,
        out int outputCount,
        [NotNullWhen(false)] out string? error
    )
    {
        Debug.Assert(!IsPending, "A callback hands its pending result to the coroutine driver instead of pushing it.");
        outputCount = 0;
        if (!IsOk)
        {
            error = _error ?? "Unknown error";
            return false;
        }

        error = null;
        try
        {
            _buffer.Push(state, luaState);
            outputCount = _buffer.Length;
            return true;
        }
        finally
        {
            _buffer.Release();
        }
    }

    /// <summary> Gives up this result without pushing it and releases captured references. </summary>
    /// <remarks> A pending result owns no captured references; its work keeps running and only a fault is observed. </remarks>
    internal void Release()
    {
        if (_pending.IsNone)
        {
            _buffer.Release();
            return;
        }

        // A pending result is dropped while its work keeps running. Its result is never pushed, so whatever it
        // captured stays tracked until the state is disposed; only a fault is observed here, without touching the
        // state from the thread that completes the work.
        _ = _pending.Task.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    private readonly struct IntoLuauCopiedBuffer(
        int length,
        IntoLuauCopied element0,
        IntoLuauCopied element1,
        IntoLuauCopied element2,
        IntoLuauCopied element3
    )
    {
        private const int MaxLength = 4;

        public readonly int Length = length is >= 0 and <= MaxLength
            ? length
            : throw new ArgumentOutOfRangeException(nameof(length));
        private readonly IntoLuauCopied _element0 = element0;
        private readonly IntoLuauCopied _element1 = element1;
        private readonly IntoLuauCopied _element2 = element2;
        private readonly IntoLuauCopied _element3 = element3;

        public unsafe void Push(LuauState state, lua_State* luaState)
        {
            if (Length > 0)
                _element0.Push(state, luaState);
            if (Length > 1)
                _element1.Push(state, luaState);
            if (Length > 2)
                _element2.Push(state, luaState);
            if (Length > 3)
                _element3.Push(state, luaState);
        }

        public void Release()
        {
            if (Length > 0)
                _element0.Release();
            if (Length > 1)
                _element1.Release();
            if (Length > 2)
                _element2.Release();
            if (Length > 3)
                _element3.Release();
        }
    }
}
