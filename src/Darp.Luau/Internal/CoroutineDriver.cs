using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Native;
using Darp.Luau.Utils;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Internal;

/// <summary>
/// Drives one host resume of a coroutine: resumes it, awaits the work of managed callbacks that suspended it, and
/// resumes it again with their results until it yields on its own, finishes, or fails.
/// </summary>
/// <remarks>
/// <para>
/// A drive is a value and allocates nothing as long as the coroutine does not suspend. It holds its own registry
/// root on the coroutine, so the coroutine stays alive even when every host handle to it is disposed while it runs
/// or awaits.
/// </para>
/// <para>
/// An async drive is registered in the <see cref="AsyncDriveTable"/> of its state. A managed callback running on
/// the coroutine finds it there to hand over its pending work (<see cref="TryAwait"/>) and to read the cancellation
/// token. A sync drive is not registered: its callbacks cannot suspend the coroutine.
/// </para>
/// <para>
/// A cancelled token stops the script at its next safepoint (<see cref="ScriptInterrupt"/>) and finishes the
/// coroutine. Awaited work is not stopped: the driver awaits it as is, and the token only reaches it through the
/// callback. Work that ends with <see cref="OperationCanceledException"/> after the token was cancelled finishes
/// the coroutine too.
/// </para>
/// <para>
/// Only the driver continues a coroutine that waits in a managed callback. Luau fails any other resume of it and
/// clears its thread data, and a script may close it. The driver notices both before it delivers the result of the
/// work and fails the host call instead.
/// </para>
/// </remarks>
internal readonly struct CoroutineDriver
{
    public const string AwaitRejectedError =
        "async managed callback requires an async host invocation (InvokeAsync, ExecuteAsync or ResumeAsync)";

    public const string AwaitNotYieldableError =
        "async managed callback cannot suspend the script here: Luau cannot yield across this call";

    private const string CoroutineLostError =
        "Lua invocation failed: a script resumed or closed the coroutine while it awaited a managed callback";

    private const int NoSlot = -1;

    private readonly LuauState _state;
    private readonly unsafe lua_State* _coroutine;
    private readonly int _coroutineRoot;
    private readonly int _argumentCount;
    private readonly int _minResultCount;
    private readonly CancellationToken _cancellationToken;

    // The slot of an async drive in the state's AsyncDriveTable.
    private readonly int _slot;

    private unsafe CoroutineDriver(
        LuauState state,
        lua_State* coroutine,
        int coroutineRoot,
        int argumentCount,
        bool allowsAwait,
        int minResultCount,
        CancellationToken cancellationToken
    )
    {
        _state = state;
        _coroutine = coroutine;
        _coroutineRoot = coroutineRoot;
        _argumentCount = argumentCount;
        _minResultCount = minResultCount;
        _cancellationToken = cancellationToken;
        _slot = allowsAwait ? state.AsyncDrives.Add(coroutine, cancellationToken) : NoSlot;
    }

    /// <summary> Starts a host resume of an existing coroutine. Pushes the resume arguments onto it. </summary>
    /// <exception cref="InvalidOperationException">Thrown when the coroutine cannot be resumed.</exception>
    public static unsafe CoroutineDriver StartResume(
        LuauState state,
        ulong coroutineHandle,
        scoped in RefEnumerable<IntoLuau> args,
        bool allowsAwait,
        CancellationToken cancellationToken
    )
    {
        lua_State* coroutine = RootResumableCoroutine(state, coroutineHandle, out int coroutineRoot);
        return Start(state, coroutine, coroutineRoot, args, allowsAwait, minResultCount: 0, cancellationToken);
    }

    /// <inheritdoc cref="StartResume(LuauState, ulong, in RefEnumerable{IntoLuau}, bool, CancellationToken)"/>
    public static unsafe CoroutineDriver StartResume(
        LuauState state,
        ulong coroutineHandle,
        IntoLuauCopied[] args,
        bool allowsAwait,
        CancellationToken cancellationToken
    )
    {
        lua_State* coroutine = RootResumableCoroutine(state, coroutineHandle, out int coroutineRoot);
        return Start(state, coroutine, coroutineRoot, args, allowsAwait, minResultCount: 0, cancellationToken);
    }

    /// <summary>
    /// Starts an async invocation of the function on top of the main stack. Pops the function and runs it with
    /// <paramref name="args"/> on a new coroutine that lives as long as the invocation.
    /// </summary>
    /// <param name="state">The state the function belongs to.</param>
    /// <param name="args">The arguments passed to the function.</param>
    /// <param name="minResultCount">Missing results up to this count are read as <c>nil</c>.</param>
    /// <param name="cancellationToken">Available to managed callbacks while the invocation runs.</param>
    public static unsafe CoroutineDriver StartInvocation(
        LuauState state,
        scoped in RefEnumerable<IntoLuau> args,
        int minResultCount,
        CancellationToken cancellationToken
    )
    {
        lua_State* coroutine = CreateInvocationCoroutine(state, out int coroutineRoot);
        return Start(state, coroutine, coroutineRoot, args, allowsAwait: true, minResultCount, cancellationToken);
    }

    /// <inheritdoc cref="StartInvocation(LuauState, in RefEnumerable{IntoLuau}, int, CancellationToken)"/>
    public static unsafe CoroutineDriver StartInvocation(
        LuauState state,
        IntoLuauCopied[] args,
        int minResultCount,
        CancellationToken cancellationToken
    )
    {
        lua_State* coroutine = CreateInvocationCoroutine(state, out int coroutineRoot);
        return Start(state, coroutine, coroutineRoot, args, allowsAwait: true, minResultCount, cancellationToken);
    }

    /// <summary> Pushes <paramref name="args"/> onto the rooted coroutine and hands its root to the drive. </summary>
    private static unsafe CoroutineDriver Start(
        LuauState state,
        lua_State* coroutine,
        int coroutineRoot,
        scoped in RefEnumerable<IntoLuau> args,
        bool allowsAwait,
        int minResultCount,
        CancellationToken cancellationToken
    )
    {
        try
        {
            int argumentCount = PushArguments(state, coroutine, args);
            return new CoroutineDriver(
                state,
                coroutine,
                coroutineRoot,
                argumentCount,
                allowsAwait,
                minResultCount,
                cancellationToken
            );
        }
        catch
        {
            state.ReferenceTracker.ReleaseRoot(coroutineRoot);
            throw;
        }
    }

    /// <inheritdoc cref="Start(LuauState, lua_State*, int, in RefEnumerable{IntoLuau}, bool, int, CancellationToken)"/>
    private static unsafe CoroutineDriver Start(
        LuauState state,
        lua_State* coroutine,
        int coroutineRoot,
        IntoLuauCopied[] args,
        bool allowsAwait,
        int minResultCount,
        CancellationToken cancellationToken
    )
    {
        try
        {
            int argumentCount = PushArguments(state, coroutine, args);
            return new CoroutineDriver(
                state,
                coroutine,
                coroutineRoot,
                argumentCount,
                allowsAwait,
                minResultCount,
                cancellationToken
            );
        }
        catch
        {
            state.ReferenceTracker.ReleaseRoot(coroutineRoot);
            throw;
        }
    }

    /// <summary> Roots the coroutine behind <paramref name="coroutineHandle"/> for a drive. </summary>
    /// <exception cref="InvalidOperationException">Thrown when the coroutine cannot be resumed.</exception>
    private static unsafe lua_State* RootResumableCoroutine(
        LuauState state,
        ulong coroutineHandle,
        out int coroutineRoot
    )
    {
        RegistryReferenceTracker.TrackedReference reference = state.GetTrackedReferenceOrThrow(coroutineHandle);
        using PopDisposable _ = reference.PushToTop();
        lua_State* coroutine = lua_tothread(state.L, -1);
        Debug.Assert(coroutine is not null);
        if (state.AsyncDrives.IsAwaiting(coroutine))
            throw new InvalidOperationException("coroutine is awaiting a managed callback");
        LuauCoroutineStatus status = GetStatus(state, coroutine);
        if (status is not LuauCoroutineStatus.Suspended)
            throw new InvalidOperationException($"cannot resume a coroutine with status {status}");
        coroutineRoot = state.ReferenceTracker.AddRoot(state.L, -1);
        return coroutine;
    }

    /// <summary> Moves the function on top of the main stack onto a new, rooted coroutine. </summary>
    private static unsafe lua_State* CreateInvocationCoroutine(LuauState state, out int coroutineRoot)
    {
        lua_State* L = state.L;
        lua_State* coroutine = lua_newthread(L); // [function, coroutine]
        coroutineRoot = state.ReferenceTracker.AddRoot(L, -1);
        lua_pop(L, 1); // [function]
        lua_xmove(L, coroutine, 1); // []
        return coroutine;
    }

    /// <summary> Called by a managed callback that returned a pending result. </summary>
    /// <returns>
    /// <c>true</c> when the calling coroutine is driven by an async host call and the callback may suspend it;
    /// the driver then awaits the pending work.
    /// </returns>
    public static unsafe bool TryAwait(LuauState state, lua_State* luaState, in LuauReturn result)
    {
        Debug.Assert(result.IsPending);
        return lua_isyieldable(luaState) != 0 && state.AsyncDrives.TrySetPending(luaState, result.Pending);
    }

    public static unsafe LuauCoroutineStatus GetStatus(LuauState state, lua_State* coroutine)
    {
        // A coroutine that waits in a managed callback is suspended too, but only its driver can resume it.
        return (lua_CoStatus)lua_costatus(state.L, coroutine) switch
        {
            lua_CoStatus.LUA_COSUS => LuauCoroutineStatus.Suspended,
            lua_CoStatus.LUA_CORUN or lua_CoStatus.LUA_CONOR => LuauCoroutineStatus.Running,
            lua_CoStatus.LUA_COFIN => LuauCoroutineStatus.Finished,
            _ => LuauCoroutineStatus.Error,
        };
    }

    public static unsafe lua_State* GetCoroutine(LuauState state, ulong coroutineHandle)
    {
        RegistryReferenceTracker.TrackedReference reference = state.GetTrackedReferenceOrThrow(coroutineHandle);
        using PopDisposable _ = reference.PushToTop();
        lua_State* coroutine = lua_tothread(state.L, -1);
        Debug.Assert(coroutine is not null);
        return coroutine;
    }

    /// <summary> Runs the drive to its end without awaiting. Managed callbacks cannot suspend the coroutine. </summary>
    public TResult Run<TResult>(Func<LuauArgs, TResult> resultSelector)
    {
        Debug.Assert(_slot == NoSlot);
        try
        {
            return ReadResults(Resume(_argumentCount), resultSelector, yieldIsError: false);
        }
        finally
        {
            End();
        }
    }

    /// <summary> Runs the drive to its end, awaiting the work of managed callbacks that suspend the coroutine. </summary>
    /// <param name="resultSelector">Reads the values the coroutine yielded or returned.</param>
    /// <param name="yieldIsError">
    /// Whether a plain script yield ends the drive with an error (invocations) instead of returning its values.
    /// </param>
    [SuppressMessage(
        "Reliability",
        "CA2007:Consider calling ConfigureAwait on the awaited task",
        Justification = "The coroutine continues where the host awaits, which honors its synchronization context."
    )]
    public async ValueTask<TResult> RunAsync<TResult>(Func<LuauArgs, TResult> resultSelector, bool yieldIsError)
    {
        Debug.Assert(_slot != NoSlot);
        AsyncDriveTable drives = _state.AsyncDrives;
        try
        {
            int status = Resume(_argumentCount);
            // The work stays pending while awaiting: the coroutine is suspended, not running.
            while (status == (int)lua_Status.LUA_YIELD && drives.GetPending(_slot) is { IsNone: false } pending)
            {
                // Assigned after the await only, so that it does not become a field of the state machine.
                Exception? failure;
                try
                {
                    await pending.Task;
                    failure = null;
                }
                catch (OperationCanceledException) when (drives.IsCancellationRequested(_slot))
                {
                    // The host cancelled its call. That ends the coroutine and the call; a script pcall cannot
                    // catch it. Work that is cancelled for any other reason fails like other work.
                    ThrowIfStateDisposed();
                    if (IsWaiting())
                        Finish();
                    throw;
                }
                catch (Exception exception)
                {
                    failure = exception;
                }

                LuauReturn result = failure is null
                    ? GetResult(pending)
                    : LuauReturn.Error(LuauStateMarshal.FormatCallbackException(failure));
                drives.ClearPending(_slot);
                status = Continue(result);
            }
            return ReadResults(status, resultSelector, yieldIsError);
        }
        finally
        {
            End();
        }
    }

    private static unsafe int PushArguments(LuauState state, lua_State* coroutine, IntoLuauCopied[] args)
    {
        int topBeforePush = lua_gettop(coroutine);
        try
        {
            foreach (IntoLuauCopied arg in args)
                arg.Push(state, coroutine);
        }
        catch
        {
            lua_settop(coroutine, topBeforePush);
            throw;
        }
        return args.Length;
    }

    private static unsafe int PushArguments(
        LuauState state,
        lua_State* coroutine,
        scoped in RefEnumerable<IntoLuau> args
    )
    {
        int topBeforePush = lua_gettop(coroutine);
        try
        {
            for (int i = 0; i < args.Length; i++)
                args[i].Push(state, coroutine);
        }
        catch
        {
            lua_settop(coroutine, topBeforePush);
            throw;
        }
        return args.Length;
    }

    private unsafe int Resume(int argumentCount)
    {
        using ScriptInterrupt.Scope _ = ScriptInterrupt.Enter(_state, _cancellationToken);
        return LuauVm.Resume(_state, _coroutine, null, argumentCount);
    }

    private void ThrowIfStateDisposed()
    {
        if (_state.IsDisposed)
            throw new ObjectDisposedException(nameof(LuauState), "The LuauState was disposed while awaiting.");
    }

    /// <summary> Resets the suspended coroutine, so it is finished. </summary>
    private unsafe void Finish() => lua_resetthread(_coroutine);

    /// <summary> Whether the coroutine still waits in the managed callback whose work this drive awaits. </summary>
    private unsafe bool IsWaiting() =>
        // The slot is compared: after this drive lost the coroutine, another drive may have taken it.
        AsyncDriveTable.IsDriving(_coroutine, _slot)
        && (lua_CoStatus)lua_costatus(_state.L, _coroutine) is lua_CoStatus.LUA_COSUS;

    /// <summary> Throws when the result of the awaited work can no longer be delivered. </summary>
    private void ThrowIfNotWaiting()
    {
        ThrowIfStateDisposed();
        if (!IsWaiting())
            throw new LuaException(CoroutineLostError);
    }

    /// <summary> Reads the result of completed work. A failing conversion is a failure of the callback. </summary>
    private LuauReturn GetResult(PendingWork pending)
    {
        // A conversion may use the state, and its result is for a callback that still waits.
        try
        {
            ThrowIfNotWaiting();
        }
        catch
        {
            pending.DropResult();
            throw;
        }

        try
        {
            return LuauReturn.FromCompleted(pending.GetResult());
        }
        catch (Exception exception)
        {
            return LuauReturn.Error(LuauStateMarshal.FormatCallbackException(exception));
        }
    }

    /// <summary> Resumes the suspended callback with the result of its awaited work. </summary>
    private unsafe int Continue(LuauReturn result)
    {
        // The conversion that made the result may have disposed the state or run a script.
        try
        {
            ThrowIfNotWaiting();
        }
        catch
        {
            result.Release();
            throw;
        }

        using ScriptInterrupt.Scope _ = ScriptInterrupt.Enter(_state, _cancellationToken);
        const int topBeforePush = 0; // The native callback trampoline yields zero values.
        string? error;
        try
        {
            if (result.TryPushValues(_state, _coroutine, out int resultCount, out error))
                return LuauVm.ResumeCallback(_state, _coroutine, resultCount);
        }
        catch (Exception exception)
        {
            error = LuauStateMarshal.FormatCallbackException(exception);
        }

        // The error surfaces at the suspended call site, where a script pcall can catch it.
        lua_settop(_coroutine, topBeforePush);
        LuauStateMarshal.PushString(_coroutine, error);
        return LuauVm.ResumeError(_state, _coroutine);
    }

    private unsafe TResult ReadResults<TResult>(int status, Func<LuauArgs, TResult> resultSelector, bool yieldIsError)
    {
        switch ((lua_Status)status)
        {
            case lua_Status.LUA_OK:
            case lua_Status.LUA_YIELD when !yieldIsError:
                try
                {
                    int resultCount = lua_gettop(_coroutine);
                    if (resultCount < _minResultCount)
                    {
                        lua_settop(_coroutine, _minResultCount);
                        resultCount = _minResultCount;
                    }
                    return resultSelector(new LuauArgs(_state, _coroutine, resultCount, 1));
                }
                finally
                {
                    // An empty stack lets lua_costatus tell a finished coroutine from one that has not started.
                    lua_settop(_coroutine, 0);
                }
            case lua_Status.LUA_YIELD:
                // The call is over. A script that kept the coroutine must not be able to continue it later.
                Finish();
                throw new LuaException("Lua invocation lua_resume failed: attempt to yield from outside a coroutine");
            case lua_Status.LUA_BREAK:
                // Only the interrupt hook breaks a coroutine. Nothing of the script may run again.
                Finish();
                throw new OperationCanceledException(
                    "The script was stopped because its host call was cancelled.",
                    // A drive without a token of its own was stopped by the token of the host call around it.
                    _cancellationToken.CanBeCanceled
                        ? _cancellationToken
                        : _state.InterruptToken
                );
            default:
                LuaException.ThrowIfNotOk(_coroutine, status, "lua_resume");
                throw new UnreachableException();
        }
    }

    private unsafe void End()
    {
        // After the state is disposed, the coroutine memory is gone; only managed cleanup is left.
        if (_slot != NoSlot)
            _state.AsyncDrives.Remove(_slot, _state.IsDisposed ? null : _coroutine);
        _state.ReferenceTracker.ReleaseRoot(_coroutineRoot);
    }
}
