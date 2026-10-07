using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Internal;
using Darp.Luau.Native;

namespace Darp.Luau;

/// <summary>
/// Represents the return value of a managed Luau callback.
/// Use <see cref="Ok()"/> or one of the <see cref="Ok(IntoLuau)"/> overloads for successful results,
/// <see cref="Error(string)"/> to return an error, or <see cref="Await(ValueTask{LuauReturn})"/> to finish later.
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
    private readonly Task<LuauReturn>? _pending;

    /// <summary> Gets whether this callback result is successful. </summary>
    public bool IsOk { get; }

    /// <summary> Gets whether this callback result completes later. See <see cref="Await(ValueTask{LuauReturn})"/>. </summary>
    public bool IsPending => _pending is not null;

    /// <summary> Used to indicate that a callback intentionally did not handle a request. </summary>
    internal const string NotHandled = "__DARP_NOT_HANDLED__";

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

    private LuauReturn(Task<LuauReturn> pending)
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

    /// <summary> Creates a callback result that completes when <paramref name="pending"/> completes. </summary>
    /// <param name="pending">The work that produces the actual result.</param>
    /// <returns>
    /// The result of <paramref name="pending"/> when it has already completed successfully; otherwise a pending result.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A pending result suspends the calling coroutine until <paramref name="pending"/> completes. This requires an
    /// async host invocation: <c>InvokeAsync</c>, <c>ExecuteAsync</c> or <c>ResumeAsync</c>.
    /// Anywhere else, the script receives a Luau error; the work keeps running and its result is dropped.
    /// </para>
    /// <para>
    /// Read every argument before the first <c>await</c>: <see cref="LuauArgs"/> and borrowed views end with the
    /// callback. Promote values you need afterwards with <c>ToOwned()</c>.
    /// </para>
    /// <para>
    /// Work that ends with <see cref="OperationCanceledException"/> cancels the host call and finishes the
    /// coroutine. Any other exception becomes a Luau error at the call site.
    /// </para>
    /// </remarks>
    public static LuauReturn Await(ValueTask<LuauReturn> pending) =>
        pending.IsCompletedSuccessfully ? FromCompleted(pending.Result) : new LuauReturn(pending.AsTask());

    /// <summary>
    /// Creates a callback result that signals the member or method is not handled.
    /// </summary>
    public static LuauReturn NotHandledError => Error(NotHandled);

    /// <summary> The work a pending result waits for, or <c>null</c>. </summary>
    internal Task<LuauReturn>? Pending => _pending;

    /// <summary> Unwraps the result of completed pending work. A result that is pending itself is rejected. </summary>
    internal static LuauReturn FromCompleted(LuauReturn result)
    {
        if (!result.IsPending)
            return result;
        result.Release();
        return Error("nested await: the result of LuauReturn.Await must not be pending itself");
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
        outputCount = 0;
        if (IsPending)
        {
            Release();
            error = "LuauReturn.Await is only supported as the result of a managed function";
            return false;
        }
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
        if (_pending is null)
        {
            _buffer.Release();
            return;
        }

        // A pending result is dropped while its work keeps running. Its result is never pushed, so whatever it
        // captured stays tracked until the state is disposed; only a fault is observed here, without touching the
        // state from the thread that completes the work.
        _ = _pending.ContinueWith(
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
