using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Darp.Luau.Internal;
using Darp.Luau.Utils;

namespace Darp.Luau;

internal struct LuauUserdataNative
{
    public const int Tag = 1;

    public GCHandle UserdataHandle { get; internal set; }
}

/// <summary>
/// Represents an owned Luau userdata reference stored in the registry.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1815:Override equals and operator equals on value types",
    Justification = "This wrapper is an ownership handle; custom value equality would imply Lua identity semantics the API does not guarantee."
)]
public readonly struct LuauUserdata : ILuauReference
{
    private readonly LuauState? _state;
    private readonly ulong _handle;

    /// <inheritdoc/>
    public bool IsDisposed => !_state.IsReferenceValid(_handle);

    /// <summary>
    /// Do not initialize directly. Create userdata through <see cref="LuauState"/> APIs.
    /// </summary>
    [Obsolete("Do not initialize the LuauUserdata. Create using the LuauState instead", true)]
    public LuauUserdata() { }

    internal LuauUserdata(LuauState state, ulong handle)
    {
        _state = state;
        _handle = handle;
    }

    /// <summary>
    /// Attempts to resolve this userdata reference back to the managed userdata instance.
    /// </summary>
    /// <typeparam name="T">Managed userdata type.</typeparam>
    /// <param name="value">Receives the managed instance when successful.</param>
    /// <param name="error">Receives a descriptive error when resolution fails.</param>
    /// <returns>
    /// <c>true</c> when this reference points to managed userdata of type <typeparamref name="T"/>;
    /// otherwise <c>false</c>.
    /// </returns>
    public bool TryGetManaged<T>([NotNullWhen(true)] out T? value, [NotNullWhen(false)] out string? error)
        where T : class, ILuauUserdata<T>
    {
        value = null;
        error = null;
        return _state.TryGetTrackedReference(_handle, out RegistryReferenceTracker.TrackedReference? reference)
            && LuauUserdataAccessCore.TryGetManaged(reference, out value, out error);
    }

    /// <summary>
    /// Converts this userdata reference to an <see cref="IntoLuau"/> value.
    /// </summary>
    /// <param name="value">The userdata reference.</param>
    /// <returns>A temporary Luau argument that borrows the same underlying userdata.</returns>
    public static implicit operator IntoLuau(LuauUserdata value) => IntoLuau.Borrow(value._state, value._handle);

    /// <inheritdoc/>
    public LuauValue DisposeAndToLuauValue() => LuauValue.Move(_state, _handle, LuauValueType.Userdata);

    /// <inheritdoc />
    public override string ToString() => Helpers.HandleToString(_state, _handle);

    /// <inheritdoc />
    public void Dispose() => _state?.ReferenceTracker.ReleaseRef(_handle);
}
