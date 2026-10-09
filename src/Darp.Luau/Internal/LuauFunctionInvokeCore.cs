using System.Runtime.CompilerServices;
using Darp.Luau.Native;
using Darp.Luau.Utils;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Internal;

internal static unsafe class LuauFunctionInvokeCore
{
    private const int LuaMultRet = -1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Invoke<T>(scoped in T? source, scoped in RefEnumerable<IntoLuau> args)
        where T : IReferenceSource, allows ref struct
    {
        LuauState state = source.Validate();
        lua_State* L = state.L;
#if DEBUG
        using var guard = new StackGuard(L, expectedDelta: 0);
#endif
        int topBeforeInvoke = lua_gettop(L);
        try
        {
            source.PushToTop();
            int length = args.Length;
            for (int i = 0; i < length; i++)
                args[i].Push(state);

            int status = LuauVm.PCall(state, L, length, 0);
            ScriptInterrupt.ThrowIfNotOk(state, L, status, "lua_pcall");
        }
        finally
        {
            lua_settop(L, topBeforeInvoke);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TR Invoke<T, TR>(scoped in T? source, scoped in RefEnumerable<IntoLuau> args, Func<LuauArgs, TR> func)
        where T : IReferenceSource, allows ref struct
    {
        LuauState state = source.Validate();
        lua_State* L = state.L;
#if DEBUG
        using var guard = new StackGuard(L, expectedDelta: 0);
#endif
        int topBeforeInvoke = lua_gettop(L);
        try
        {
            source.PushToTop();
            int nArgs = args.Length;
            for (int i = 0; i < nArgs; i++)
                args[i].Push(state);

            int status = LuauVm.PCall(state, L, nArgs, LuaMultRet);
            ScriptInterrupt.ThrowIfNotOk(state, L, status, "lua_pcall");
            var result = new LuauArgs(state, lua_gettop(L) - topBeforeInvoke, topBeforeInvoke + 1);
            return func(result);
        }
        finally
        {
            lua_settop(L, topBeforeInvoke);
        }
    }

    /// <summary> Invokes an owned function on a new coroutine, so managed callbacks can await. </summary>
    /// <remarks>
    /// Runs inline when the state is idle or the caller already executes a turn. Otherwise the start is queued with
    /// copies of the arguments; reference arguments must stay alive until the returned task completes.
    /// </remarks>
    public static ValueTask<TR> InvokeAsync<TR>(
        LuauState? state,
        ulong functionHandle,
        scoped in RefEnumerable<IntoLuau> args,
        Func<LuauArgs, TR> resultSelector,
        CancellationToken cancellationToken
    )
    {
        LuauSynchronizationContext context = state
            .GetTrackedReferenceOrThrow(functionHandle)
            .ValidateInternal()
            .AsyncContext;
        if (context.TryEnter(out LuauSynchronizationContext.Turn turn))
        {
            using (turn)
            {
                // Resolved again inside the turn: until it began, another turn may have run.
                RegistryReferenceTracker.TrackedReference function = state.GetTrackedReferenceOrThrow(functionHandle);
                return InvokeInTurn(function, args, resultSelector, cancellationToken);
            }
        }

        return QueueInvocation(
            state,
            functionHandle,
            IntoLuau.CaptureBorrowed(args),
            resultSelector,
            cancellationToken
        );
    }

    // Separate from InvokeAsync, so that only a queued start allocates the closure.
    private static ValueTask<TR> QueueInvocation<TR>(
        LuauState state,
        ulong functionHandle,
        IntoLuauCopied[] copiedArgs,
        Func<LuauArgs, TR> resultSelector,
        CancellationToken cancellationToken
    ) =>
        state.AsyncContext.Queue(() =>
        {
            RegistryReferenceTracker.TrackedReference queuedFunction = state.GetTrackedReferenceOrThrow(functionHandle);
            LuauState validState = queuedFunction.ValidateInternal();
#pragma warning disable CA2000 // The function is moved onto the coroutine by StartInvocation.
            _ = queuedFunction.PushToTop();
#pragma warning restore CA2000
            return CoroutineDriver
                .StartInvocation(validState, copiedArgs, minResultCount: 0, cancellationToken)
                .RunAsync(resultSelector, yieldIsError: true);
        });

    /// <summary> Invokes a borrowed function on a new coroutine, so managed callbacks can await. </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when another thread executes the state: a borrowed view is only valid inside its callback's turn.
    /// </exception>
    public static ValueTask<TR> InvokeAsync<T, TR>(
        scoped in T? source,
        scoped in RefEnumerable<IntoLuau> args,
        Func<LuauArgs, TR> resultSelector,
        CancellationToken cancellationToken
    )
        where T : IReferenceSource, allows ref struct
    {
        LuauState state = source.Validate();
        if (!state.AsyncContext.TryEnter(out LuauSynchronizationContext.Turn turn))
            throw new InvalidOperationException("A borrowed function can only be invoked during its callback.");
        using (turn)
            return InvokeInTurn(source, args, resultSelector, cancellationToken);
    }

    private static ValueTask<TR> InvokeInTurn<T, TR>(
        scoped in T? source,
        scoped in RefEnumerable<IntoLuau> args,
        Func<LuauArgs, TR> resultSelector,
        CancellationToken cancellationToken
    )
        where T : IReferenceSource, allows ref struct
    {
        LuauState state = source.Validate();
#if DEBUG
        using var guard = new StackGuard(state.L, expectedDelta: 0);
#endif
#pragma warning disable CA2000 // The function is moved onto the coroutine by StartInvocation.
        _ = source.PushToTop();
#pragma warning restore CA2000
        return LuauSynchronizationContext.Detach(
            CoroutineDriver
                .StartInvocation(state, args, minResultCount: 0, cancellationToken)
                .RunAsync(resultSelector, yieldIsError: true)
        );
    }

    /// <summary> Completes when <paramref name="operation"/> completes, without its placeholder result. </summary>
    public static ValueTask WithoutResult(ValueTask<bool> operation)
    {
        if (!operation.IsCompletedSuccessfully)
            return new ValueTask(operation.AsTask());
        _ = operation.Result;
        return ValueTask.CompletedTask;
    }

    /// <summary> Ignores all results. Used where no result is requested. </summary>
    public static bool IgnoreResults(LuauArgs a) => false;

    public static TR ResultSelector<TR>(LuauArgs a) => a.Read<TR>(1);

    // A result that was read holds a reference when it is a table, function or other reference type. When a later
    // result cannot be read, the caller gets none of them, so the ones already read are released here.

    public static (TR1, TR2) ResultSelector<TR1, TR2>(LuauArgs a)
    {
        TR1 result1 = a.Read<TR1>(1);
        try
        {
            return (result1, a.Read<TR2>(2));
        }
        catch
        {
            Release(result1);
            throw;
        }
    }

    public static (TR1, TR2, TR3) ResultSelector<TR1, TR2, TR3>(LuauArgs a)
    {
        (TR1 result1, TR2 result2) = ResultSelector<TR1, TR2>(a);
        try
        {
            return (result1, result2, a.Read<TR3>(3));
        }
        catch
        {
            Release(result1);
            Release(result2);
            throw;
        }
    }

    public static (TR1, TR2, TR3, TR4) ResultSelector<TR1, TR2, TR3, TR4>(LuauArgs a)
    {
        (TR1 result1, TR2 result2, TR3 result3) = ResultSelector<TR1, TR2, TR3>(a);
        try
        {
            return (result1, result2, result3, a.Read<TR4>(4));
        }
        catch
        {
            Release(result1);
            Release(result2);
            Release(result3);
            throw;
        }
    }

    public static LuauValue[] ResultSelectorMulti(LuauArgs a)
    {
        var values = new LuauValue[a.ArgumentCount];
        try
        {
            for (int i = 1; i <= values.Length; i++)
            {
                if (!a.TryReadLuauValue(i, out LuauValue value, out string? error))
                    throw new ArgumentOutOfRangeException(nameof(a), error);
                values[i - 1] = value;
            }
        }
        catch
        {
            // Reading a value that has no managed form throws. Entries that were not read are nil.
            foreach (LuauValue read in values)
                read.Dispose();
            throw;
        }

        return values;
    }

    private static void Release<T>(T result)
    {
        if (result is IDisposable disposable)
            disposable.Dispose();
    }
}
