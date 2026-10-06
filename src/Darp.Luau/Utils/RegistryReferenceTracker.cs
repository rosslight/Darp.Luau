using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Native;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Utils;

internal sealed class RegistryReferenceTracker(LuauState state)
{
    private readonly LuauState _state = state;
    private readonly ConcurrentDictionary<ulong, TrackedReference> _trackedReferences = [];
    private ulong _nextTrackedReferenceHandle = 1;
    private ulong _releasedRegistryReferenceCount;

    public LuauMemoryStatistics GetStatistics(int activeManagedCallbacks) =>
        new(
            ActiveRegistryReferences: (ulong)_trackedReferences.Count,
            CreatedRegistryReferences: _nextTrackedReferenceHandle - 1,
            ReleasedRegistryReferences: _releasedRegistryReferenceCount,
            ActiveManagedCallbacks: activeManagedCallbacks
        );

    /// <summary>
    /// Retrieves a reference for a value at the specified stack index in the Lua state and tracks it for future use.
    /// </summary>
    /// <param name="L">A pointer to the Lua state.</param>
    /// <param name="stackIndex">The index of the value on the Lua stack to be referenced and tracked.</param>
    /// <param name="pinned">Indicates whether the reference should be pinned to prevent garbage collection. Defaults to false.</param>
    /// <returns>An integer representing the tracked reference handle.</returns>
    /// <exception cref="InvalidOperationException"> Thrown if the maximum number of tracked registry references has been exceeded. </exception>
    public unsafe ulong TrackRef(lua_State* L, int stackIndex, bool pinned = false)
    {
        ArgumentNullException.ThrowIfNull(L);
        if (!_state.OwnsThread(L))
            throw new InvalidOperationException("Cross-state reference tracking is not allowed.");
#if DEBUG
        using var guard = new StackGuard(L, expectedDelta: 0);
#endif
        int luaReference = lua_ref(L, stackIndex);

        ulong handle = GetNextHandle();
        _trackedReferences[handle] = new TrackedReference(_state, luaReference, handle, pinned);
        return handle;
    }

    public unsafe ulong TrackAndPopRef(lua_State* L, int stackIndex, bool pinned = false)
    {
#if DEBUG
        using var guard = new StackGuard(L, expectedDelta: -1);
#endif
        ulong handle = TrackRef(L, stackIndex, pinned);
        lua_pop(L, 1);
        return handle;
    }

    public bool HasRegistryReference(ulong handle) => _trackedReferences.ContainsKey(handle);

    public ulong CountRefOrThrow(ulong handle)
    {
        if (!_trackedReferences.TryGetValue(handle, out TrackedReference? trackedReference))
            throw new ObjectDisposedException(nameof(handle), "Tried to access a reference that is no longer tracked.");

        // A pinned reference is never released so there is no need to track it multiple times
        if (trackedReference.IsPinned)
            return handle;
        return trackedReference.CountRef();
    }

    public void ReleaseRef(ulong handle)
    {
        if (_state.IsDisposed)
            return;
        if (!_trackedReferences.TryGetValue(handle, out TrackedReference? trackedReference))
            return;
        if (trackedReference.IsPinned)
            return;
        trackedReference.RemoveRef(handle);
    }

    public void ReleaseAll()
    {
        if (_trackedReferences.IsEmpty)
            return;

        _releasedRegistryReferenceCount += (ulong)_trackedReferences.Count;
        _trackedReferences.Clear();
    }

    private ulong GetNextHandle()
    {
        if (_nextTrackedReferenceHandle == ulong.MaxValue)
            throw new InvalidOperationException("Too many tracked registry references were created for this state.");
        ulong nextHandle = _nextTrackedReferenceHandle;
        _nextTrackedReferenceHandle++;
        return nextHandle;
    }

    public bool TryGetTrackedReference(ulong handle, [NotNullWhen(true)] out TrackedReference? trackedReference) =>
        _trackedReferences.TryGetValue(handle, out trackedReference);

    internal sealed class TrackedReference(LuauState state, int luaReference, ulong originalHandle, bool isPinned)
        : IReferenceSource
    {
        private readonly LuauState _state = state;
        private readonly int _luaReference = luaReference;
        private readonly ulong _originalHandle = originalHandle;
        private int _numberOfManagedRefs = 1;

        public bool IsPinned { get; } = isPinned;

        public LuauState ValidateInternal()
        {
            _state.ThrowIfDisposed();
            return _state;
        }

        public unsafe PopDisposable PushToStack(out int stackIndex)
        {
            _state.ThrowIfDisposed();
            lua_State* L = _state.L;
            var type = (lua_Type)lua_getref(L, _luaReference);
            stackIndex = lua_gettop(L);
            return new PopDisposable(L, true);
        }

        public unsafe PopDisposable PushToTop()
        {
            _state.ThrowIfDisposed();
            lua_State* L = _state.L;
            var type = (lua_Type)lua_getref(L, _luaReference);
            return new PopDisposable(L, true);
        }

        public ulong CountRef()
        {
            if (IsPinned)
                return _originalHandle;
            ulong nextHandle = _state.ReferenceTracker.GetNextHandle();
            _state.ReferenceTracker._trackedReferences[nextHandle] = this;
            _numberOfManagedRefs++;
            return nextHandle;
        }

        public unsafe void RemoveRef(ulong handle)
        {
            if (!_state.ReferenceTracker._trackedReferences.TryRemove(handle, out TrackedReference? reference))
                throw new InvalidOperationException("Tried to remove a handle that was not tracked.");
            if (reference != this)
                throw new InvalidOperationException("Tried to remove a handle that does not belong to this reference.");

            _numberOfManagedRefs--;
            if (_numberOfManagedRefs > 0)
                return;

            _state.ThrowIfDisposed();
            lua_State* L = _state.L;
            _ = lua_unref(L, _luaReference);
            _state.ReferenceTracker._releasedRegistryReferenceCount++;
        }
    }
}
