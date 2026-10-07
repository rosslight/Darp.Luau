using Darp.Luau.Native;
using Darp.Luau.Utils;

namespace Darp.Luau;

/// <summary>
/// Represents a borrowed, stack-bound Luau coroutine value read from callback arguments.
/// </summary>
/// <remarks>
/// This view does not own a registry reference.
/// It is valid only while the originating callback frame is active on the same <see cref="LuauState"/>.
/// Using it after the callback frame ends throws <see cref="ObjectDisposedException"/>.
/// Promote it with <see cref="ToOwned"/> to resume the coroutine.
/// </remarks>
public readonly ref struct LuauCoroutineView : ILuauView<LuauCoroutine>
{
    private readonly StackReference _reference;

    internal unsafe LuauCoroutineView(LuauState state, lua_State* luaState, int stackIndex) =>
        _reference = new StackReference(state, luaState, stackIndex);

    /// <inheritdoc/>
    public LuauCoroutine ToOwned()
    {
        LuauState state = _reference.ValidateInternal();
        return new LuauCoroutine(state, ReferenceSourceExtensions.ToOwnedHandle(_reference));
    }

    /// <summary>
    /// Converts this borrowed coroutine view to an <see cref="IntoLuau"/> value without creating an owned reference.
    /// </summary>
    /// <param name="value">The borrowed coroutine view.</param>
    /// <returns>A temporary representation with the same callback-frame lifetime constraints.</returns>
    public static implicit operator IntoLuau(LuauCoroutineView value) => IntoLuau.FromRefSource(value._reference);

    /// <inheritdoc />
    public override string ToString() => _reference.ToString();
}
