using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Darp.Luau.Internal;

/// <summary> A call of the host into a state, from the first value it pushes to the last one it reads. </summary>
/// <remarks>
/// <para>
/// A host call opens a scope before it touches the VM. The scope settles what the call takes over from the calls
/// around it and what it brings itself:
/// </para>
/// <list type="bullet">
/// <item>
/// The turn. A sync call takes it, or fails when another thread executes one. Made inside a turn, from a callback
/// or the code after its captured <c>await</c>, it is part of that turn and enters for free. An async call is in a
/// turn before it gets here: <see cref="LuauSynchronizationContext"/> admitted or queued it.
/// </item>
/// <item>
/// The token that stops the script (<see cref="ScriptInterrupt"/>). An async call installs its own while it runs
/// a script. A sync call takes none and leaves the one in charge as it is, so a sync call made by a callback is
/// stopped like the async call that runs the callback.
/// </item>
/// </list>
/// </remarks>
internal static class HostCall
{
    /// <summary> Begins a sync host call. </summary>
    /// <exception cref="InvalidOperationException">Thrown when another thread executes a turn of the state.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when the state is disposed.</exception>
    // Not inlined: next to a call that runs a script, a call into this method is nothing, and its caller stays
    // small enough for the JIT to inline what the call spends its time in.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static SyncScope EnterSync(LuauState state) => EnterSyncInlined(state);

    /// <summary> <see cref="EnterSync"/> for a table access, which is short enough for a call to show. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SyncScope EnterSyncInlined(LuauState state)
    {
        LuauSynchronizationContext context = state.AsyncContext;
        if (!context.EnterOrThrow())
            return default;
        // Checked inside the turn: until it began, another thread could dispose the state.
        if (state.IsDisposed)
            ThrowDisposed(context);
        return new SyncScope(context);
    }

    /// <summary>
    /// Begins a stretch of an async host call in which it runs a script: <paramref name="cancellationToken"/>,
    /// and no other token, stops the script while the scope is open.
    /// </summary>
    /// <remarks>
    /// Every async host call is stopped by its own token only. One that runs inside a callback of another one, or
    /// continues there after its awaited work completed, is not stopped by the token of that other call.
    /// </remarks>
    public static AsyncScope EnterAsync(LuauState state, CancellationToken cancellationToken)
    {
        Debug.Assert(state.AsyncContext.IsEntered, "An async host call runs in a turn.");
        CancellationToken enclosingToken = state.InterruptToken;
        ScriptInterrupt.Install(state, cancellationToken);
        return new AsyncScope(state, enclosingToken);
    }

    [DoesNotReturn]
    private static void ThrowDisposed(LuauSynchronizationContext context)
    {
        context.EndTurn();
        throw new ObjectDisposedException(typeof(LuauState).FullName);
    }

    /// <summary> Gives the turn back when the sync host call that took it is over. </summary>
    /// <remarks> The scope of a call that was made inside a turn ends nothing. </remarks>
    public readonly ref struct SyncScope
    {
        private readonly LuauSynchronizationContext? _turn;

        internal SyncScope(LuauSynchronizationContext turn) => _turn = turn;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose() => _turn?.EndTurn();
    }

    /// <summary> Restores the token of the enclosing host call when the script has returned to the host. </summary>
    public readonly ref struct AsyncScope
    {
        [SuppressMessage(
            "Usage",
            "CA2213:Disposable fields should be disposed",
            Justification = "The scope references LuauState but does not own it."
        )]
        private readonly LuauState? _state;
        private readonly CancellationToken _enclosingToken;

        internal AsyncScope(LuauState state, CancellationToken enclosingToken)
        {
            _state = state;
            _enclosingToken = enclosingToken;
        }

        public void Dispose()
        {
            if (_state is not null)
                ScriptInterrupt.Install(_state, _enclosingToken);
        }
    }
}
