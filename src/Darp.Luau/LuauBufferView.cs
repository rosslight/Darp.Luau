using Darp.Luau.Internal;
using Darp.Luau.Native;
using Darp.Luau.Utils;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau;

/// <summary> A borrowed view of a Luau buffer, such as a buffer argument of a managed callback. </summary>
/// <remarks>
/// The view does not own a registry reference. It is valid only while the callback that received it runs; call
/// <see cref="ToOwned"/> to keep the buffer.
/// </remarks>
public readonly unsafe ref struct LuauBufferView
{
    private readonly LuauSlot _slot;

    internal LuauBufferView(LuauState state, lua_State* luaState, int stackIndex) =>
        _slot = new LuauSlot(state, luaState, stackIndex);

    internal LuauBufferView(LuauSlot slot) => _slot = slot;

    /// <summary> Gets the bytes of the buffer without copying them. </summary>
    /// <returns>A span over memory that Luau owns.</returns>
    /// <remarks>
    /// The span is valid as long as the view. Use <see cref="ToArray"/> for bytes with a normal .NET lifetime.
    /// </remarks>
    public ReadOnlySpan<byte> AsSpan()
    {
        LuauState state = _slot.State;
        lua_State* L = state.L;
        _slot.Push(state);
        // What the view borrows keeps the buffer alive after its copy is popped.
        ReadOnlySpan<byte> bytes = LuauReader.BufferBytes(L, -1);
        lua_pop(L, 1);
        return bytes;
    }

    /// <summary> Copies the bytes of the buffer into a managed array. </summary>
    public byte[] ToArray() => AsSpan().ToArray();

    /// <summary> Creates an owned reference to the buffer, which stays valid after the callback. </summary>
    /// <returns>The owned reference, which the caller disposes.</returns>
    public LuauBuffer ToOwned() => new(_slot.State, _slot.ToOwnedHandle());

    /// <summary> Passes the buffer on to Luau without creating an owned reference. </summary>
    /// <param name="value">The borrowed buffer.</param>
    /// <returns>A temporary representation that is valid as long as the view.</returns>
    public static implicit operator IntoLuau(LuauBufferView value) => IntoLuau.FromSlot(value._slot);

    /// <summary> Gets the bytes of the buffer as hexadecimal text. </summary>
    public override string ToString() => Convert.ToHexString(AsSpan());
}
