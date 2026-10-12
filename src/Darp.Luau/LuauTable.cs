using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Native;
using Darp.Luau.Utils;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau;

/// <summary>
/// Represents an owned Luau table reference stored in the registry.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1815:Override equals and operator equals on value types",
    Justification = "This wrapper is an ownership handle; custom value equality would imply Lua identity semantics the API does not guarantee."
)]
public readonly unsafe struct LuauTable : IDisposable, IEnumerable<KeyValuePair<LuauValue, LuauValue>>
{
    private readonly LuauState? _state;
    private readonly ulong _handle;

    /// <summary> Gets whether this reference was disposed, or its state was. </summary>
    public bool IsDisposed => !_state.IsReferenceValid(_handle);

    /// <summary>
    /// Do not initialize directly. Create tables through <see cref="LuauState"/> APIs.
    /// </summary>
    [Obsolete("Do not initialize the LuauTable. Create using the LuauState instead", true)]
    public LuauTable() { }

    internal LuauTable(LuauState? state, ulong handle)
    {
        _state = state;
        _handle = handle;
    }

    internal LuauState GetStateOrThrow()
    {
        ArgumentNullException.ThrowIfNull(_state);
        _state.ThrowIfDisposed();
        return _state;
    }

    internal ulong GetHandleOrThrow()
    {
        _ = GetStateOrThrow().GetTrackedReferenceOrThrow(_handle);
        return _handle;
    }

    /// <inheritdoc cref="LuauTableView.ListCount"/>
    /// <exception cref="ObjectDisposedException">Thrown when this reference was disposed.</exception>
    public int ListCount => AsView().ListCount;

    /// <inheritdoc cref="LuauTableView.Get{T}"/>
    /// <exception cref="ObjectDisposedException">Thrown when this reference was disposed.</exception>
    public T Get<T>(IntoLuau key) => AsView().Get<T>(key);

    /// <inheritdoc cref="LuauTableView.GetOrNull{T}"/>
    /// <exception cref="ObjectDisposedException">Thrown when this reference was disposed.</exception>
    public T? GetOrNull<T>(IntoLuau key)
        where T : class => AsView().GetOrNull<T>(key);

    /// <inheritdoc cref="LuauTableView.TryGet{T}"/>
    /// <exception cref="ObjectDisposedException">Thrown when this reference was disposed.</exception>
    public bool TryGet<T>(IntoLuau key, [NotNullWhen(true)] out T? value) => AsView().TryGet(key, out value);

    /// <inheritdoc cref="LuauTableView.TryGetOrNull{T}"/>
    /// <exception cref="ObjectDisposedException">Thrown when this reference was disposed.</exception>
    public bool TryGetOrNull<T>(IntoLuau key, out T? value)
        where T : class => AsView().TryGetOrNull(key, out value);

    /// <inheritdoc cref="LuauTableView.Set"/>
    /// <exception cref="ObjectDisposedException">Thrown when this reference was disposed.</exception>
    public void Set(IntoLuau key, IntoLuau value) => AsView().Set(key, value);

    /// <inheritdoc cref="LuauTableView.ContainsKey"/>
    /// <exception cref="ObjectDisposedException">Thrown when this reference was disposed.</exception>
    public bool ContainsKey(IntoLuau key) => AsView().ContainsKey(key);

    /// <summary>
    /// Converts this table reference to an <see cref="IntoLuau"/> value.
    /// </summary>
    /// <param name="value">The table reference.</param>
    /// <returns>A temporary Luau argument that borrows the same underlying table.</returns>
    public static implicit operator IntoLuau(LuauTable value) => IntoLuau.Borrow(value._state, value._handle);

    /// <summary> Hands this reference over to a <see cref="LuauValue"/>, which then holds it. </summary>
    /// <returns>A value that keeps the table alive until it is disposed.</returns>
    /// <remarks> This wrapper must not be used afterwards. </remarks>
    public LuauValue DisposeAndToLuauValue() => LuauValue.Move(_state, _handle, LuauValueType.Table);

    /// <inheritdoc />
    public override string ToString() => Helpers.HandleToString(_state, _handle);

    /// <inheritdoc cref="IEnumerable{T}.GetEnumerator"/>
    public Enumerator GetEnumerator()
    {
        _state.ThrowIfDisposed();
        ulong newHandle = _state.ReferenceTracker.CountRefOrThrow(_handle);
        return new Enumerator(_state, newHandle);
    }

    IEnumerator<KeyValuePair<LuauValue, LuauValue>> IEnumerable<KeyValuePair<LuauValue, LuauValue>>.GetEnumerator() =>
        GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc cref="LuauTableView.this[IntoLuau]"/>
    /// <exception cref="ObjectDisposedException">Thrown when this reference was disposed.</exception>
    [SuppressMessage(
        "Design",
        "CA1043:Use integral or string argument for indexers",
        Justification = "Lua tables support arbitrary key types, and IntoLuau models that domain directly."
    )]
    public LuauValue this[IntoLuau key] => AsView()[key];

    /// <summary>
    /// Returns an <c>ipairs</c>-style enumerable over consecutive integer keys starting at <c>1</c>.
    /// </summary>
    /// <returns>An enumerable of index-value pairs.</returns>
    /// <remarks>Enumeration stops at the first <c>nil</c> entry.</remarks>
    public TableIPairsEnumerable IPairs() => new(this, _state, _handle);

    /// <summary>
    /// Releases this table reference from the state registry.
    /// </summary>
    public void Dispose() => _state?.ReferenceTracker.ReleaseRef(_handle);

    /// <summary> A view of this table, which is valid while this reference is. </summary>
    internal LuauTableView AsView() => new(_state.GetSlotOrThrow(_handle));

    /// <summary> The enumerator of the <see cref="TableIPairsEnumerable"/> </summary>
    public struct Enumerator : IEnumerator<KeyValuePair<LuauValue, LuauValue>>
    {
        private readonly LuauState? _state;
        private readonly ulong _handle;
        private KeyValuePair<LuauValue, LuauValue> _current;

        // Where lua_rawiter continues. The enumerator holds nothing in Luau besides its table reference, so a copy
        // of it has nothing to release twice.
        private int _iterator;

        /// <inheritdoc />
        public KeyValuePair<LuauValue, LuauValue> Current => _current;

        object IEnumerator.Current => _current;

        /// <summary>Initializes an enumerator over a tracked table reference.</summary>
        /// <param name="state">The state that owns the tracked table reference.</param>
        /// <param name="handle">The tracked table handle to enumerate.</param>
        internal Enumerator(LuauState? state, ulong handle)
        {
            _state = state;
            _handle = handle;
        }

        /// <inheritdoc />
        public bool MoveNext()
        {
            RegistryReferenceTracker.TrackedReference trackedReference = _state.GetTrackedReferenceOrThrow(_handle);
            lua_State* L = _state.L;
#if DEBUG
            using var guard = new StackGuard(L, expectedDelta: 0);
#endif
            using PopDisposable tablePop = trackedReference.PushToTop();
            int t = lua_gettop(L); // table index

            // lua_rawiter pushes (key, value) unless it is at the end. Unlike lua_next it cannot raise an error
            // when the table changed since the last step.
            int next = lua_rawiter(L, t, _iterator);
            if (next < 0)
                return false;
            _iterator = next;

            // stack: [table, key, value]
            var value = LuauValue.ToValue(_state);
            lua_pop(L, 1); // pop value
            var key = LuauValue.ToValue(_state);
            lua_pop(L, 1); // pop key; table is released by tablePop

            _current = new KeyValuePair<LuauValue, LuauValue>(key, value);
            return true;
        }

        /// <inheritdoc />
        public void Reset()
        {
            _iterator = 0;
            _current = default;
        }

        void IDisposable.Dispose()
        {
            Reset();
            _state?.ReferenceTracker.ReleaseRef(_handle);
        }
    }
}
