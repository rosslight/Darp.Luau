using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Internal;
using Darp.Luau.Native;
using Darp.Luau.Utils;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau;

/// <summary> A borrowed view of a Luau userdata, such as a userdata argument of a managed callback. </summary>
/// <remarks>
/// The view does not own a registry reference. It is valid only while the callback that received it runs; call
/// <see cref="ToOwned"/> to keep the userdata.
/// </remarks>
public readonly unsafe ref struct LuauUserdataView
{
    private readonly LuauSlot _slot;

    internal LuauUserdataView(LuauState state, lua_State* luaState, int stackIndex) =>
        _slot = new LuauSlot(state, luaState, stackIndex);

    internal LuauUserdataView(LuauSlot slot) => _slot = slot;

    /// <summary> Gets the managed object this userdata stands for. </summary>
    /// <typeparam name="T">The type of the object: its userdata type, a base type or an interface of it.</typeparam>
    /// <returns>The object.</returns>
    /// <exception cref="LuaGetException">Thrown when the userdata is not managed userdata of type <typeparamref name="T"/>.</exception>
    public T Get<T>()
        where T : class =>
        TryRead<T>(isProbe: false, out var value, out ReadFailure failure)
            ? value
            : throw failure.ToException("userdata");

    /// <summary> Attempts to get the managed object this userdata stands for. </summary>
    /// <typeparam name="T">The type of the object: its userdata type, a base type or an interface of it.</typeparam>
    /// <param name="value">The object when the userdata is managed userdata of type <typeparamref name="T"/>.</param>
    /// <returns><c>true</c> when the userdata is managed userdata of type <typeparamref name="T"/>; otherwise <c>false</c>.</returns>
    public bool TryGet<T>([NotNullWhen(true)] out T? value)
        where T : class => TryRead(isProbe: true, out value, out _);

    /// <summary> Creates an owned reference to the userdata, which stays valid after the callback. </summary>
    /// <returns>The owned reference, which the caller disposes.</returns>
    public LuauUserdata ToOwned() => new(_slot.State, _slot.ToOwnedHandle());

    /// <summary> Passes the userdata on to Luau without creating an owned reference. </summary>
    /// <param name="value">The borrowed userdata.</param>
    /// <returns>A temporary representation that is valid as long as the view.</returns>
    public static implicit operator IntoLuau(LuauUserdataView value) => IntoLuau.FromSlot(value._slot);

    /// <inheritdoc />
    public override string ToString() => _slot.ToString();

    private bool TryRead<T>(bool isProbe, [NotNullWhen(true)] out T? value, out ReadFailure failure)
        where T : class
    {
        LuauState state = _slot.State;
        lua_State* L = state.L;
        _slot.Push(state);
        ReadSource source = ReadSource.FromStack(state, L, -1);
        bool isRead = LuauReader.TryRead(isProbe ? source.AsProbe() : source, out value, out failure);
        lua_pop(L, 1);
        return isRead;
    }
}
