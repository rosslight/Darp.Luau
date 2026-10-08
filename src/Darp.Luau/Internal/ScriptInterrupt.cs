using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Darp.Luau.Native;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Internal;

/// <summary> Stops a running script when the token of the async host call that runs it is cancelled. </summary>
/// <remarks>
/// <para>
/// Luau asks at its safepoints whether to stop: when a loop jumps back, at calls and returns, and in the string
/// pattern matcher. While an async host call with a cancellable token runs a script, the question is answered with
/// that token. Once it is cancelled, the native library stops the script in the way that is possible there:
/// </para>
/// <list type="bullet">
/// <item>
/// Where Luau can yield, it breaks the coroutine: Luau leaves it where it is and returns to the host, which
/// abandons it. A break is not an error, so no <c>pcall</c> of the script sees it.
/// </item>
/// <item>
/// Where Luau cannot yield, in a metamethod, a <c>table.sort</c> comparator or a sync host call such as
/// <c>Invoke</c>, it raises a Luau error. A script can catch that error, but the token stays cancelled, so the
/// error is raised again at the next safepoint and the script cannot go on.
/// </item>
/// </list>
/// <para>
/// The token is the only thing another thread touches. Everything else, including the answer to Luau, runs on the
/// thread that runs the script.
/// </para>
/// </remarks>
internal static unsafe class ScriptInterrupt
{
    private const string StoppedMessage = "The script was stopped because its host call was cancelled.";

    /// <summary> Creates what Luau asks at its safepoints. The state owns it and frees it with <see cref="Free"/>. </summary>
    /// <param name="stateHandle">A handle to the state, which outlives the returned memory.</param>
    public static darp_luau_interrupt* Create(GCHandle stateHandle)
    {
        var interrupt = (darp_luau_interrupt*)NativeMemory.Alloc((nuint)sizeof(darp_luau_interrupt));
        interrupt->callback = &ShouldStop;
        interrupt->ctx = (void*)GCHandle.ToIntPtr(stateHandle);
        return interrupt;
    }

    public static void Free(darp_luau_interrupt* interrupt) => NativeMemory.Free(interrupt);

    /// <summary> Lets <paramref name="cancellationToken"/>, and no other token, stop the script while the scope is open. </summary>
    /// <remarks>
    /// Every async host call is stopped by its own token only. One that runs inside a callback of another one, or
    /// continues there after its awaited work completed, is not stopped by the token of that other call.
    /// </remarks>
    public static Scope Enter(LuauState state, CancellationToken cancellationToken)
    {
        CancellationToken enclosingToken = state.InterruptToken;
        Install(state, cancellationToken);
        return new Scope(state, enclosingToken);
    }

    private static void Install(LuauState state, CancellationToken cancellationToken)
    {
        // Luau is only made to ask for a token that can be cancelled: a script pays for every question.
        if (cancellationToken.CanBeCanceled)
            darp_luau_setinterrupt(state.L, state.Interrupt);
        else if (state.InterruptToken.CanBeCanceled)
            darp_luau_setinterrupt(state.L, null);
        state.InterruptToken = cancellationToken;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ShouldStop(lua_State* L, void* ctx) =>
        GCHandle.FromIntPtr((nint)ctx).Target is LuauState state && state.InterruptToken.IsCancellationRequested
            ? 1
            : 0;

    /// <summary> Throws when a sync host call failed. </summary>
    /// <remarks>
    /// A sync host call takes no token. Made by a callback, it runs under the token of the async host call that
    /// runs the callback, and fails as cancelled like that call when the token is cancelled.
    /// </remarks>
    /// <exception cref="LuaException">Thrown when <paramref name="status"/> is not successful.</exception>
    /// <exception cref="OperationCanceledException">Thrown instead when the token that stops the script is cancelled.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfNotOk(LuauState state, lua_State* L, int status, string description)
    {
        if (status != 0)
            ThrowFailure(state, L, status, description);
    }

    private static void ThrowFailure(LuauState state, lua_State* L, int status, string description)
    {
        try
        {
            LuaException.ThrowIfNotOk(L, status, description);
        }
        catch (LuaException failure) when (state.InterruptToken.IsCancellationRequested)
        {
            throw Cancelled(failure, state.InterruptToken);
        }
    }

    /// <summary> The failure of a host call whose token is cancelled. </summary>
    /// <param name="failure">
    /// The error that ended the script, if any. Where Luau cannot break, stopping a script surfaces as an error,
    /// which cannot be told from an error the script made on its own.
    /// </param>
    /// <param name="cancellationToken">The cancelled token.</param>
    public static OperationCanceledException Cancelled(LuaException? failure, CancellationToken cancellationToken) =>
        new(StoppedMessage, failure, cancellationToken);

    /// <summary> Restores the token of the enclosing host call when the script has returned to the host. </summary>
    public readonly ref struct Scope(LuauState state, CancellationToken enclosingToken)
    {
        [SuppressMessage(
            "Usage",
            "CA2213:Disposable fields should be disposed",
            Justification = "The scope references LuauState but does not own it."
        )]
        private readonly LuauState _state = state;
        private readonly CancellationToken _enclosingToken = enclosingToken;

        public void Dispose() => Install(_state, _enclosingToken);
    }
}
