using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
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
/// While a drive is in progress, the driver is attached to the coroutine as its thread data. A managed callback
/// running on that coroutine finds it there to hand over its pending work (<see cref="TryAwait"/>) and to read the
/// cancellation token. The driver holds its own registry reference on the coroutine, so the coroutine stays alive
/// even when every host handle to it is disposed during an await.
/// </para>
/// <para>
/// Cancellation is cooperative: the driver awaits the work as is, and the token only reaches it through the
/// callback. Work that ends with <see cref="OperationCanceledException"/> finishes the coroutine.
/// </para>
/// <para>
/// Sync resumes use the same driver without permission to await.
/// </para>
/// </remarks>
internal sealed class CoroutineDriver
{
    public const string AwaitRejectedError =
        "async managed callback requires an async host invocation (InvokeAsync, ExecuteAsync or ResumeAsync)";

    private readonly LuauState _state;
    private readonly unsafe lua_State* _coroutine;
    private readonly ulong _coroutineHandle;
    private readonly int _argumentCount;
    private readonly bool _allowsAwait;
    private readonly int _minResultCount;
    private GCHandle _selfHandle;
    private Task<LuauReturn>? _pending;

    private unsafe CoroutineDriver(
        LuauState state,
        lua_State* coroutine,
        ulong coroutineHandle,
        int argumentCount,
        bool allowsAwait,
        int minResultCount,
        CancellationToken cancellationToken
    )
    {
        _state = state;
        _coroutine = coroutine;
        _coroutineHandle = coroutineHandle;
        _argumentCount = argumentCount;
        _allowsAwait = allowsAwait;
        _minResultCount = minResultCount;
        CancellationToken = cancellationToken;
        _selfHandle = GCHandle.Alloc(this);
        lua_setthreaddata(coroutine, (void*)GCHandle.ToIntPtr(_selfHandle));
    }

    /// <summary> The token of the host call driving the coroutine. </summary>
    public CancellationToken CancellationToken { get; }

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
        lua_State* coroutine = GetCoroutine(state, coroutineHandle);
        if (FromCoroutine(coroutine) is { _pending: not null })
            throw new InvalidOperationException("coroutine is awaiting a managed callback");
        LuauCoroutineStatus status = GetStatus(state, coroutine);
        if (status is not LuauCoroutineStatus.Suspended)
            throw new InvalidOperationException($"cannot resume a coroutine with status {status}");

        int argumentCount = PushArguments(state, coroutine, args);
        ulong driverHandle = state.ReferenceTracker.CountRefOrThrow(coroutineHandle);
        return new CoroutineDriver(
            state,
            coroutine,
            driverHandle,
            argumentCount,
            allowsAwait,
            minResultCount: 0,
            cancellationToken
        );
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
        lua_State* L = state.L;
        lua_State* coroutine = lua_newthread(L); // [function, coroutine]
        ulong coroutineHandle = state.ReferenceTracker.TrackAndPopRef(L, -1); // [function]
        lua_xmove(L, coroutine, 1); // []
        int argumentCount;
        try
        {
            argumentCount = PushArguments(state, coroutine, args);
        }
        catch
        {
            state.ReferenceTracker.ReleaseRef(coroutineHandle);
            throw;
        }
        return new CoroutineDriver(
            state,
            coroutine,
            coroutineHandle,
            argumentCount,
            allowsAwait: true,
            minResultCount,
            cancellationToken
        );
    }

    /// <summary> Called by a managed callback that returned a pending result. </summary>
    /// <returns>
    /// <c>true</c> when the calling coroutine is driven by an async host call and the callback may suspend it;
    /// the driver then awaits the pending work.
    /// </returns>
    public static unsafe bool TryAwait(lua_State* luaState, in LuauReturn result)
    {
        Debug.Assert(result.Pending is not null);
        if (lua_isyieldable(luaState) == 0 || FromCoroutine(luaState) is not { _allowsAwait: true } driver)
            return false;
        driver._pending = result.Pending;
        return true;
    }

    /// <summary> Returns the driver currently driving <paramref name="luaState"/>, if any. </summary>
    public static unsafe CoroutineDriver? FromCoroutine(lua_State* luaState)
    {
        void* threadData = lua_getthreaddata(luaState);
        return threadData is null ? null : GCHandle.FromIntPtr((nint)threadData).Target as CoroutineDriver;
    }

    public static unsafe LuauCoroutineStatus GetStatus(LuauState state, lua_State* coroutine)
    {
        // A coroutine suspended in an awaiting managed callback is yielded, but only its driver can resume it.
        if (FromCoroutine(coroutine) is { _pending: not null })
            return LuauCoroutineStatus.Suspended;

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
        Debug.Assert(!_allowsAwait);
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
        Debug.Assert(_allowsAwait);
        try
        {
            int status = Resume(_argumentCount);
            // _pending stays set while awaiting: the coroutine is suspended, not running.
            while (status == (int)lua_Status.LUA_YIELD && _pending is { } pending)
            {
                LuauReturn result;
                try
                {
                    result = LuauReturn.FromCompleted(await pending);
                }
                catch (OperationCanceledException)
                {
                    // Cancellation ends the coroutine and the host call; a script pcall cannot catch it.
                    ThrowIfStateDisposed();
                    Finish();
                    throw;
                }
                catch (Exception exception)
                {
                    result = LuauReturn.Error(LuauStateMarshal.FormatCallbackException(exception));
                }
                _pending = null;
                status = Continue(result);
            }
            return ReadResults(status, resultSelector, yieldIsError);
        }
        finally
        {
            End();
        }
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

    private unsafe int Resume(int argumentCount) => lua_resume(_coroutine, null, argumentCount);

    private void ThrowIfStateDisposed()
    {
        if (_state.IsDisposed)
            throw new ObjectDisposedException(nameof(LuauState), "The LuauState was disposed while awaiting.");
    }

    /// <summary> Resets the suspended coroutine, so it is finished. </summary>
    private unsafe void Finish() => lua_resetthread(_coroutine);

    /// <summary> Resumes the suspended callback with the result of its awaited work. </summary>
    private unsafe int Continue(LuauReturn result)
    {
        if (_state.IsDisposed)
        {
            result.Release();
            ThrowIfStateDisposed();
        }

        int topBeforePush = lua_gettop(_coroutine);
        string? error;
        try
        {
            if (result.TryPushValues(_state, _coroutine, out int resultCount, out error))
                return Resume(resultCount);
        }
        catch (Exception exception)
        {
            error = LuauStateMarshal.FormatCallbackException(exception);
        }

        // The error surfaces at the suspended call site, where a script pcall can catch it.
        lua_settop(_coroutine, topBeforePush);
        LuauStateMarshal.PushString(_coroutine, error);
        return lua_resumeerror(_coroutine, null);
    }

    private unsafe TResult ReadResults<TResult>(int status, Func<LuauArgs, TResult> resultSelector, bool yieldIsError)
    {
        switch ((lua_Status)status)
        {
            case lua_Status.LUA_OK:
            case lua_Status.LUA_YIELD when !yieldIsError:
                try
                {
                    if (lua_gettop(_coroutine) < _minResultCount)
                        lua_settop(_coroutine, _minResultCount);
                    return resultSelector(new LuauArgs(_state, _coroutine, lua_gettop(_coroutine), 1));
                }
                finally
                {
                    // An empty stack lets lua_costatus tell a finished coroutine from one that has not started.
                    lua_settop(_coroutine, 0);
                }
            case lua_Status.LUA_YIELD:
                throw new LuaException("Lua invocation lua_resume failed: attempt to yield from outside a coroutine");
            case lua_Status.LUA_BREAK:
                throw new LuaException("Lua invocation lua_resume failed: coroutine was interrupted");
            default:
                LuaException.ThrowIfNotOk(_coroutine, status, "lua_resume");
                throw new UnreachableException();
        }
    }

    private unsafe void End()
    {
        // After the state is disposed, the coroutine memory is gone; only managed cleanup is left.
        if (!_state.IsDisposed)
        {
            lua_setthreaddata(_coroutine, null);
            _state.ReferenceTracker.ReleaseRef(_coroutineHandle);
        }
        _selfHandle.Free();
    }
}
