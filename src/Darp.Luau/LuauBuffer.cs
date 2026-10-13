using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Utils;

namespace Darp.Luau;

/// <summary>
/// Represents an owned Luau buffer reference stored in the registry.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1815:Override equals and operator equals on value types",
    Justification = "This wrapper is an ownership handle; custom value equality would imply Lua identity semantics the API does not guarantee."
)]
public readonly struct LuauBuffer : IDisposable
{
    private readonly LuauState? _state;
    private readonly ulong _handle;

    /// <summary> Gets whether this reference was disposed, or its state was. </summary>
    public bool IsDisposed => !_state.IsReferenceValid(_handle);

    /// <summary>
    /// Do not initialize directly. Create buffers through <see cref="LuauState"/> APIs.
    /// </summary>
    [Obsolete("Do not initialize the LuauBuffer. Create using the LuauState instead", true)]
    public LuauBuffer() { }

    internal LuauBuffer(LuauState? state, ulong handle)
    {
        _state = state;
        _handle = handle;
    }

    /// <summary> Gets the bytes of the buffer without copying them. </summary>
    /// <returns>A span over memory that Luau owns.</returns>
    /// <remarks>
    /// The span is valid until this reference is disposed. Use <see cref="ToArray"/> for bytes with a normal .NET
    /// lifetime.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when this reference was disposed.</exception>
    public ReadOnlySpan<byte> AsSpan() => AsView().AsSpan();

    /// <summary> Copies the bytes of the buffer into a managed array. </summary>
    /// <exception cref="ObjectDisposedException">Thrown when this reference was disposed.</exception>
    public byte[] ToArray() => AsView().ToArray();

    /// <summary>
    /// Converts this buffer reference to an <see cref="IntoLuau"/> value.
    /// </summary>
    /// <param name="value">The buffer reference.</param>
    /// <returns>A temporary Luau argument that borrows the same underlying buffer.</returns>
    public static implicit operator IntoLuau(LuauBuffer value) => IntoLuau.Borrow(value._state, value._handle);

    /// <summary> Hands this reference over to a <see cref="LuauValue"/>, which then holds it. </summary>
    /// <returns>A value that keeps the buffer alive until it is disposed.</returns>
    /// <remarks> This wrapper must not be used afterwards. </remarks>
    public LuauValue DisposeAndToLuauValue() => LuauValue.Move(_state, _handle, LuauValueType.Buffer);

    /// <summary> Gets the bytes of the buffer as hexadecimal text. </summary>
    /// <returns>The text, or a placeholder when this reference was disposed.</returns>
    public override string ToString() => IsDisposed ? "<disposed>" : AsView().ToString();

    /// <summary>
    /// Releases this buffer reference from the state registry.
    /// </summary>
    public void Dispose() => _state?.ReferenceTracker.ReleaseRef(_handle);

    internal LuauBufferView AsView() => new(_state.GetSlotOrThrow(_handle));
}
