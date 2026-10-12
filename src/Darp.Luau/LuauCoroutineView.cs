using Darp.Luau.Native;
using Darp.Luau.Utils;

namespace Darp.Luau;

/// <summary> A borrowed view of a Luau coroutine, such as a coroutine argument of a managed callback. </summary>
/// <remarks>
/// The view does not own a registry reference. It is valid only while the callback that received it runs. Resuming
/// needs an owned reference: call <see cref="ToOwned"/>.
/// </remarks>
public readonly ref struct LuauCoroutineView
{
    private readonly LuauSlot _slot;

    internal unsafe LuauCoroutineView(LuauState state, lua_State* luaState, int stackIndex) =>
        _slot = new LuauSlot(state, luaState, stackIndex);

    /// <summary> Creates an owned reference to the coroutine, which stays valid after the callback. </summary>
    /// <returns>The owned reference, which the caller disposes.</returns>
    public LuauCoroutine ToOwned() => new(_slot.State, _slot.ToOwnedHandle());

    /// <summary> Passes the coroutine on to Luau without creating an owned reference. </summary>
    /// <param name="value">The borrowed coroutine.</param>
    /// <returns>A temporary representation that is valid as long as the view.</returns>
    public static implicit operator IntoLuau(LuauCoroutineView value) => IntoLuau.FromSlot(value._slot);

    /// <inheritdoc />
    public override string ToString() => _slot.ToString();
}
