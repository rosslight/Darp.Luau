using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Internal;
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
public readonly struct LuauBuffer : ILuauReference
{
    private readonly LuauState? _state;
    private readonly ulong _handle;

    /// <inheritdoc/>
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

    /// <summary>
    /// Attempts to get a read-only span over the underlying Luau buffer bytes.
    /// </summary>
    /// <param name="bytes">Receives a span over Luau-owned memory when successful.</param>
    /// <returns><c>true</c> when the buffer is still valid; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// The returned span aliases Luau memory and should be consumed immediately.
    /// Copy the data if it must outlive the callback frame.
    /// </remarks>
    public bool TryGet(out ReadOnlySpan<byte> bytes)
    {
        bytes = default;
        return _state.TryGetTrackedReference(_handle, out RegistryReferenceTracker.TrackedReference? reference)
            && LuauBufferAccessCore.TryGet(reference, out bytes);
    }

    /// <summary>
    /// Attempts to copy the underlying Luau buffer bytes into a managed array.
    /// </summary>
    /// <param name="bytes">Receives the copied bytes when successful; otherwise an empty array.</param>
    /// <returns><c>true</c> when the copy succeeded; otherwise <c>false</c>.</returns>
    public bool TryGet([NotNullWhen(true)] out byte[]? bytes)
    {
        bytes = null;
        return _state.TryGetTrackedReference(_handle, out RegistryReferenceTracker.TrackedReference? reference)
            && LuauBufferAccessCore.TryGet(reference, out bytes);
    }

    /// <summary>
    /// Converts this buffer reference to an <see cref="IntoLuau"/> value.
    /// </summary>
    /// <param name="value">The buffer reference.</param>
    /// <returns>A temporary Luau argument that borrows the same underlying buffer.</returns>
    public static implicit operator IntoLuau(LuauBuffer value) => IntoLuau.Borrow(value._state, value._handle);

    /// <inheritdoc/>
    public LuauValue DisposeAndToLuauValue() => LuauValue.Move(_state, _handle, LuauValueType.Buffer);

    /// <inheritdoc />
    public override string ToString() => TryGet(out ReadOnlySpan<byte> span) ? Convert.ToHexString(span) : "<nil>";

    /// <summary>
    /// Releases this buffer reference from the state registry.
    /// </summary>
    public void Dispose() => _state?.ReferenceTracker.ReleaseRef(_handle);
}
