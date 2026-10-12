using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Utils;

namespace Darp.Luau;

/// <summary>
/// Represents an owned Luau string reference stored in the registry.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1815:Override equals and operator equals on value types",
    Justification = "This wrapper is an ownership handle; custom value equality would imply Lua identity semantics the API does not guarantee."
)]
public readonly struct LuauString : IDisposable
{
    private readonly LuauState? _state;
    private readonly ulong _handle;

    /// <summary> Gets whether this reference was disposed, or its state was. </summary>
    public bool IsDisposed => !_state.IsReferenceValid(_handle);

    /// <summary> Do not initialize directly. Create via <see cref="LuauState"/> APIs. </summary>
    [Obsolete("Do not initialize the LuauString. Create using the LuauState instead", true)]
    public LuauString() { }

    internal LuauString(LuauState? state, ulong handle)
    {
        _state = state;
        _handle = handle;
    }

    /// <summary> Gets the UTF-8 bytes of the string without copying them. </summary>
    /// <returns>A span over memory that Luau owns.</returns>
    /// <remarks>
    /// The span is valid until this reference is disposed. Use <see cref="ToString"/> for text with a normal .NET
    /// lifetime.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when this reference was disposed.</exception>
    public ReadOnlySpan<byte> AsSpan() => AsView().AsSpan();

    /// <summary> Releases this string reference from the state registry. </summary>
    public void Dispose() => _state?.ReferenceTracker.ReleaseRef(_handle);

    /// <summary> Converts this string reference into an <see cref="IntoLuau"/> value. </summary>
    public static implicit operator IntoLuau(LuauString value) => IntoLuau.Borrow(value._state, value._handle);

    /// <summary> Hands this reference over to a <see cref="LuauValue"/>, which then holds it. </summary>
    /// <returns>A value that keeps the string alive until it is disposed.</returns>
    /// <remarks> This wrapper must not be used afterwards. </remarks>
    public LuauValue DisposeAndToLuauValue() => LuauValue.Move(_state, _handle, LuauValueType.String);

    /// <summary> Gets the string as managed text. </summary>
    /// <returns>The text, or a placeholder when this reference was disposed.</returns>
    public override string ToString() => IsDisposed ? "<disposed>" : AsView().ToString();

    internal LuauStringView AsView() => new(_state.GetSlotOrThrow(_handle));
}
