using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Utils;

namespace Darp.Luau;

/// <summary> The list part of a <see cref="LuauTableView"/>: its values at <c>1</c>, <c>2</c>, ... as <typeparamref name="T"/>. </summary>
/// <typeparam name="T">The managed type the values are read as.</typeparam>
/// <remarks> Borrows the view: it is valid as long as the view and holds no reference to the table. </remarks>
public readonly ref struct TableViewIPairsEnumerable<T>
{
    private readonly LuauSlot _slot;

    internal TableViewIPairsEnumerable(LuauSlot slot) => _slot = slot;

    /// <summary> Gets an enumerator that starts at index <c>1</c>. </summary>
    public Enumerator GetEnumerator() => new(_slot);

    /// <summary> Enumerates a <see cref="TableViewIPairsEnumerable{T}"/>. </summary>
    [SuppressMessage(
        "Design",
        "CA1034:Nested types should not be visible",
        Justification = "An enumerator nested in what it enumerates is the pattern of the base library, as in Span<T>.Enumerator."
    )]
    public ref struct Enumerator
    {
        private readonly LuauSlot _slot;
        private KeyValuePair<int, T> _current;

        // An enumeration that ended stays ended, also when the value it ended at changes afterwards.
        private bool _isFinished;

        internal Enumerator(LuauSlot slot) => _slot = slot;

        /// <summary> Gets the index and the value the enumerator is at. </summary>
        public readonly KeyValuePair<int, T> Current => _current;

        /// <summary> Moves to the next index. </summary>
        /// <returns><c>false</c> when the value there is nil or not a <typeparamref name="T"/>.</returns>
        /// <exception cref="NotSupportedException">Thrown when the value cannot be read as <typeparamref name="T"/> at all.</exception>
        public bool MoveNext()
        {
            if (_isFinished)
                return false;

            int index = _current.Key + 1;
            if (LuauTableView.TryGetItem<T>(_slot, index, out var value))
            {
                _current = new KeyValuePair<int, T>(index, value);
                return true;
            }

            _isFinished = true;
            return false;
        }
    }
}
