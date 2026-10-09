using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Internal;
using Darp.Luau.Native;
using Darp.Luau.Utils;

namespace Darp.Luau;

/// <summary>
/// Represents a borrowed, stack-bound Luau userdata value read from callback arguments.
/// </summary>
/// <remarks>
/// This view does not own a registry reference.
/// It is valid only while the originating callback frame is active on the same <see cref="LuauState"/>.
/// Using it after the callback frame ends throws <see cref="ObjectDisposedException"/>.
/// </remarks>
public readonly ref struct LuauUserdataView : ILuauView<LuauUserdata>
{
    private readonly StackReference _reference;

    internal unsafe LuauUserdataView(LuauState state, lua_State* luaState, int stackIndex) =>
        _reference = new StackReference(state, luaState, stackIndex);

    /// <summary>
    /// Attempts to resolve this borrowed userdata as managed userdata of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Managed userdata type.</typeparam>
    /// <param name="value">Receives the managed userdata instance when successful.</param>
    /// <param name="error">Receives a descriptive error when resolution fails.</param>
    /// <returns>
    /// <c>true</c> when the userdata is managed userdata created by this library and matches
    /// <typeparamref name="T"/>; otherwise <c>false</c>.
    /// </returns>
    public bool TryGetManaged<T>([NotNullWhen(true)] out T? value, [NotNullWhen(false)] out string? error)
        where T : class, ILuauUserdata<T> => LuauUserdataAccessCore.TryGetManaged(_reference, out value, out error);

    /// <inheritdoc/>
    public LuauUserdata ToOwned()
    {
        LuauState state = _reference.ValidateInternal();
        return new LuauUserdata(state, ReferenceSourceExtensions.ToOwnedHandle(_reference));
    }

    /// <summary>
    /// Converts this borrowed userdata view to an <see cref="IntoLuau"/> value without creating an owned reference.
    /// </summary>
    /// <param name="value">The borrowed userdata view.</param>
    /// <returns>A temporary representation with the same callback-frame lifetime constraints.</returns>
    public static implicit operator IntoLuau(LuauUserdataView value) => IntoLuau.FromRefSource(value._reference);

    /// <inheritdoc />
    public override string ToString() => _reference.ToString();
}
