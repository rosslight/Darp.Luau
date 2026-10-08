using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Darp.Luau.Native;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Internal;

/// <summary> Stops a running script when the token of the async host call that runs it is cancelled. </summary>
/// <remarks>
/// <para>
/// Luau calls an interrupt hook at its safepoints: loop back-edges, calls and returns. While a host call with a
/// cancellable token runs a script, the hook is installed and reads that token. Once it is cancelled, the hook
/// breaks the coroutine: Luau leaves it where it is and returns to the host, which abandons it. A break is not an
/// error, so no <c>pcall</c> of the script sees it.
/// </para>
/// <para>
/// The token is the only thing another thread touches. Everything else, including the hook, runs on the thread
/// that runs the script.
/// </para>
/// <para>
/// Luau cannot break where it cannot yield: in a metamethod, a <c>table.sort</c> comparator or a sync host call
/// such as <c>Invoke</c>. There the hook does nothing. The token stays cancelled, so the script is stopped at the
/// first safepoint after that code has returned.
/// </para>
/// </remarks>
internal static unsafe class ScriptInterrupt
{
    /// <summary> Lets <paramref name="cancellationToken"/>, and no other token, stop the script while the scope is open. </summary>
    /// <remarks>
    /// Every host call is stopped by its own token only. A host call that runs inside a callback of another one,
    /// or continues there after its awaited work completed, is not stopped by the token of that other call.
    /// </remarks>
    public static Scope Enter(LuauState state, CancellationToken cancellationToken)
    {
        CancellationToken enclosingToken = state.InterruptToken;
        Install(state, cancellationToken);
        return new Scope(state, enclosingToken);
    }

    /// <summary> The hook is only installed for a token that can be cancelled: a script pays for every call of it. </summary>
    private static void Install(LuauState state, CancellationToken cancellationToken)
    {
        state.InterruptToken = cancellationToken;
        state.Callbacks->interrupt = cancellationToken.CanBeCanceled ? &OnSafepoint : null;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnSafepoint(lua_State* L, int gcState)
    {
        // Luau also reports the steps of its garbage collector, where a script cannot be stopped.
        if (gcState >= 0)
            return;
        if (GCHandle.FromIntPtr((nint)lua_callbacks(L)->userdata).Target is not LuauState state)
            return;
        if (!state.InterruptToken.IsCancellationRequested)
            return;
        // Where Luau cannot yield, lua_break raises an error instead, which must not unwind through this frame.
        if (lua_isyieldable(L) != 0)
            _ = lua_break(L);
    }

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
