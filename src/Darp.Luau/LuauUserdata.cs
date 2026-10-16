using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Darp.Luau.Utils;

namespace Darp.Luau;

/// <summary> What Luau stores in a managed userdata: where its state keeps the object. </summary>
/// <remarks>
/// The object is not held by a handle of its own: handles are scanned by every collection of the runtime, which
/// made each of them pause longer with every userdata that was alive.
/// </remarks>
internal struct LuauUserdataNative
{
    public const int Tag = 1;

    /// <summary> The weak handle of the state, which outlives every userdata of the state. </summary>
    public nint State;

    /// <summary> Where the state keeps the object. </summary>
    public int Slot;

    /// <summary> Which object of the slot is this userdata's: slots are reused once their object is released. </summary>
    public int Generation;

    /// <summary> The managed object, or null if it was released. </summary>
    public readonly object? Target =>
        GCHandle.FromIntPtr(State).Target is LuauState state ? state.UserdataObjects.Get(Slot, Generation) : null;
}

/// <summary>
/// Represents an owned Luau userdata reference stored in the registry.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1815:Override equals and operator equals on value types",
    Justification = "This wrapper is an ownership handle; custom value equality would imply Lua identity semantics the API does not guarantee."
)]
public readonly struct LuauUserdata : IDisposable
{
    private readonly LuauState? _state;
    private readonly ulong _handle;

    /// <summary> Gets whether this reference was disposed, or its state was. </summary>
    public bool IsDisposed => !_state.IsReferenceValid(_handle);

    /// <summary>
    /// Do not initialize directly. Create userdata through <see cref="LuauState"/> APIs.
    /// </summary>
    [Obsolete("Do not initialize the LuauUserdata. Create using the LuauState instead", true)]
    public LuauUserdata() { }

    internal LuauUserdata(LuauState? state, ulong handle)
    {
        _state = state;
        _handle = handle;
    }

    /// <inheritdoc cref="LuauUserdataView.Get{T}"/>
    /// <exception cref="ObjectDisposedException">Thrown when this reference was disposed.</exception>
    public T Get<T>()
        where T : class => AsView().Get<T>();

    /// <inheritdoc cref="LuauUserdataView.TryGet{T}"/>
    /// <exception cref="ObjectDisposedException">Thrown when this reference was disposed.</exception>
    public bool TryGet<T>([NotNullWhen(true)] out T? value)
        where T : class => AsView().TryGet(out value);

    /// <summary>
    /// Converts this userdata reference to an <see cref="IntoLuau"/> value.
    /// </summary>
    /// <param name="value">The userdata reference.</param>
    /// <returns>A temporary Luau argument that borrows the same underlying userdata.</returns>
    public static implicit operator IntoLuau(LuauUserdata value) => IntoLuau.Borrow(value._state, value._handle);

    /// <summary> Hands this reference over to a <see cref="LuauValue"/>, which then holds it. </summary>
    /// <returns>A value that keeps the userdata alive until it is disposed.</returns>
    /// <remarks> This wrapper must not be used afterwards. </remarks>
    public LuauValue DisposeAndToLuauValue() => LuauValue.Move(_state, _handle, LuauValueType.Userdata);

    /// <inheritdoc />
    public override string ToString() => Helpers.HandleToString(_state, _handle);

    /// <summary> Releases this userdata reference from the state registry. </summary>
    public void Dispose() => _state?.ReferenceTracker.ReleaseRef(_handle);

    internal LuauUserdataView AsView() => new(_state.GetSlotOrThrow(_handle));
}
