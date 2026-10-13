using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using Darp.Luau.Native;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Utils;

internal static class Helpers
{
    /// <summary> Throw if the <see cref="LuauState"/> is not present or disposed </summary>
    /// <param name="state"> The state to check </param>
    /// <exception cref="InvalidOperationException"> Thrown if the state is null/uninitialized </exception>
    /// <exception cref="ObjectDisposedException"> Thrown if the state is disposed </exception>
    public static void ThrowIfDisposed([NotNull] this LuauState? state)
    {
        if (state is null)
            throw new InvalidOperationException("No LuauState present.");
        ObjectDisposedException.ThrowIf(state.IsDisposed, state);
    }

    public static bool IsReferenceValid([NotNullWhen(true)] this LuauState? state, ulong handle)
    {
        return state is not null && state.ReferenceTracker.HasRegistryReference(handle);
    }

    public static RegistryReferenceTracker.TrackedReference GetTrackedReferenceOrThrow(
        [NotNull] this LuauState? state,
        ulong handle
    )
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.ReferenceTracker.TryGetTrackedReference(handle, out var trackedReference))
            return trackedReference;
        throw new ObjectDisposedException(nameof(handle), "Tried to access a reference that is no longer tracked.");
    }

    public static bool TryGetTrackedReference(
        [NotNullWhen(true)] this LuauState? state,
        ulong handle,
        [NotNullWhen(true)] out RegistryReferenceTracker.TrackedReference? trackedReference
    )
    {
        if (state is not null)
            return state.ReferenceTracker.TryGetTrackedReference(handle, out trackedReference);
        trackedReference = null;
        return false;
    }

    /// <summary> Gets the slot of the owned reference <paramref name="handle"/>, for a view of it. </summary>
    /// <exception cref="ObjectDisposedException">Thrown when the reference was disposed, or the state is.</exception>
    public static LuauSlot GetSlotOrThrow([NotNull] this LuauState? state, ulong handle)
    {
        RegistryReferenceTracker.TrackedReference reference = state.GetTrackedReferenceOrThrow(handle);
        return new LuauSlot(state, reference);
    }

    /// <summary> Describes the referenced value like Luau's <c>tostring</c> does without metamethods. </summary>
    /// <param name="state"> The state the reference is associated with </param>
    /// <param name="handle"> The tracked handle </param>
    /// <returns> The type and the address of the value, such as <c>table: 0x000001c2a4f0e8d0</c> </returns>
    public static unsafe string HandleToString(LuauState? state, ulong handle)
    {
        if (state is null || state.IsDisposed)
            return "<nil>";
        if (!state.TryGetTrackedReference(handle, out RegistryReferenceTracker.TrackedReference? trackedReference))
            return "<disposed>";
        using PopDisposable _ = trackedReference.PushToTop(); // [value]
        return StackString(state, -1);
    }

    /// <summary> Describes the value on the stack like Luau's <c>tostring</c> does without metamethods. </summary>
    /// <param name="state"> The state the stackIndex is associated with </param>
    /// <param name="stackIndex"> The stackIndex </param>
    /// <returns> The type and the address of the value, such as <c>table: 0x000001c2a4f0e8d0</c> </returns>
    /// <remarks> Runs no script code: <c>__tostring</c> or a replaced global <c>tostring</c> could raise an error. </remarks>
    public static unsafe string StackString(LuauState state, int stackIndex)
    {
        state.ThrowIfDisposed();
        lua_State* L = state.L;
        string typeName = Encoding.UTF8.GetString(
            MemoryMarshal.CreateReadOnlySpanFromNullTerminated(lua_typename(L, lua_type(L, stackIndex)))
        );
        return $"{typeName}: 0x{(nuint)lua_topointer(L, stackIndex):x16}";
    }
}
