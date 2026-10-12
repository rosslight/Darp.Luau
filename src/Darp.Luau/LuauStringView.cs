using System.Text;
using Darp.Luau.Internal;
using Darp.Luau.Native;
using Darp.Luau.Utils;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau;

/// <summary> A borrowed view of a Luau string, such as a string argument of a managed callback. </summary>
/// <remarks>
/// The view does not own a registry reference. It is valid only while the callback that received it runs; call
/// <see cref="ToOwned"/> to keep the string.
/// </remarks>
public readonly unsafe ref struct LuauStringView
{
    private readonly LuauSlot _slot;

    internal LuauStringView(LuauState state, lua_State* luaState, int stackIndex) =>
        _slot = new LuauSlot(state, luaState, stackIndex);

    internal LuauStringView(LuauSlot slot) => _slot = slot;

    /// <summary> Gets the UTF-8 bytes of the string without copying them. </summary>
    /// <returns>A span over memory that Luau owns.</returns>
    /// <remarks>
    /// The span is valid as long as the view. Use <see cref="ToString"/> for text with a normal .NET lifetime.
    /// </remarks>
    public ReadOnlySpan<byte> AsSpan()
    {
        LuauState state = _slot.State;
        lua_State* L = state.L;
        _slot.Push(state);
        // What the view borrows keeps the string alive after its copy is popped.
        ReadOnlySpan<byte> bytes = LuauReader.StringBytes(L, -1);
        lua_pop(L, 1);
        return bytes;
    }

    /// <summary> Creates an owned reference to the string, which stays valid after the callback. </summary>
    /// <returns>The owned reference, which the caller disposes.</returns>
    public LuauString ToOwned() => new(_slot.State, _slot.ToOwnedHandle());

    /// <summary> Passes the string on to Luau without creating an owned reference. </summary>
    /// <param name="value">The borrowed string.</param>
    /// <returns>A temporary representation that is valid as long as the view.</returns>
    public static implicit operator IntoLuau(LuauStringView value) => IntoLuau.FromSlot(value._slot);

    /// <summary> Gets the string as managed text. </summary>
    public override string ToString() => Encoding.UTF8.GetString(AsSpan());
}
