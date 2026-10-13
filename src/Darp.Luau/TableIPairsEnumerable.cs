using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Utils;

namespace Darp.Luau;

/// <summary> The list part of a <see cref="LuauTable"/>: its values at <c>1</c>, <c>2</c>, ... as <typeparamref name="T"/>. </summary>
/// <typeparam name="T">The managed type the values are read as.</typeparam>
[SuppressMessage(
    "Performance",
    "CA1815:Override equals and operator equals on value types",
    Justification = "This enumerable is a lightweight view over Lua state and handle ownership; custom value equality would imply semantics the API does not guarantee."
)]
public readonly struct TableIPairsEnumerable<T> : IEnumerable<KeyValuePair<int, T>>
{
    private readonly LuauTable _table;
    private readonly LuauState? _state;
    private readonly ulong _handle;

    internal TableIPairsEnumerable(LuauTable table, LuauState? state, ulong handle)
    {
        _table = table;
        _state = state;
        _handle = handle;
    }

    /// <summary>
    /// Gets the raw array length of the underlying Luau table.
    /// </summary>
    /// <remarks>
    /// If the table has holes, or values that are not a <typeparamref name="T"/>, enumeration ends earlier.
    /// </remarks>
    public int Count => _table.ListCount;

    /// <inheritdoc cref="IEnumerable{T}.GetEnumerator" />
    public Enumerator GetEnumerator()
    {
        _state.ThrowIfDisposed();
        ulong newHandle = _state.ReferenceTracker.CountRefOrThrow(_handle);
        return new Enumerator(_state, newHandle);
    }

    IEnumerator<KeyValuePair<int, T>> IEnumerable<KeyValuePair<int, T>>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary> The enumerator of the <see cref="TableIPairsEnumerable{T}"/> </summary>
    public struct Enumerator : IEnumerator<KeyValuePair<int, T>>
    {
        private readonly LuauState? _state;
        private readonly ulong _handle;
        private KeyValuePair<int, T> _current;

        /// <inheritdoc />
        public readonly KeyValuePair<int, T> Current => _current;

        readonly object IEnumerator.Current => _current;

        /// <summary>Initializes an enumerator over a tracked table reference.</summary>
        /// <param name="state">The state that owns the tracked table reference.</param>
        /// <param name="handle">The tracked table handle to enumerate.</param>
        internal Enumerator(LuauState? state, ulong handle)
        {
            _state = state;
            _handle = handle;
        }

        /// <inheritdoc />
        /// <exception cref="NotSupportedException">Thrown when the value cannot be read as <typeparamref name="T"/> at all.</exception>
        public bool MoveNext()
        {
            int index = _current.Key + 1;
            if (!LuauTableView.TryGetItem<T>(_state.GetSlotOrThrow(_handle), index, out var value))
                return false;
            _current = new KeyValuePair<int, T>(index, value);
            return true;
        }

        /// <inheritdoc />
        public void Reset() => _current = default;

        void IDisposable.Dispose()
        {
            Reset();
            _state?.ReferenceTracker.ReleaseRef(_handle);
        }
    }
}
