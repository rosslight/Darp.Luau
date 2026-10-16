namespace Darp.Luau.Utils;

/// <summary> The managed objects of the userdata of one state, in slots that the userdata refer to. </summary>
/// <remarks>
/// Only the thread that uses the state reads and writes it, also from the destructors of userdata. A slot is reused
/// once its object is released, with a new generation, so a userdata of the released object never sees the next one.
/// </remarks>
internal sealed class UserdataObjects
{
    // Below this many records, the records of open scopes are not checked for collected userdata
    private const int MinimumRecordsToCompact = 64;

    // Empty until the first userdata, so that a state that creates none allocates nothing for them
    private object?[] _objects = [];
    private int[] _generations = [];
    private readonly Stack<int> _freeSlots = [];
    private readonly Dictionary<object, int> _slotByObject = new(ReferenceEqualityComparer.Instance);

    // The userdata created in open scopes, and the open scopes with where they start in them, outermost first
    private readonly List<(int Slot, int Generation)> _createdInScopes = [];
    private readonly List<(long Id, int Start)> _scopes = [];
    private long _lastScopeId;
    private int _compactAtRecords = MinimumRecordsToCompact;
    private int _used;

    /// <summary> The number of records kept for the userdata created in open scopes. </summary>
    internal int ScopeRecordCount => _createdInScopes.Count;

    /// <summary> The object of a userdata, or null if it was released. </summary>
    public object? Get(int slot, int generation) =>
        (uint)slot < (uint)_used && _generations[slot] == generation ? _objects[slot] : null;

    /// <summary> The slot of the userdata an object was last pushed as, if that userdata may still be alive. </summary>
    public bool TryGetSlot(object value, out int slot) => _slotByObject.TryGetValue(value, out slot);

    /// <summary> Keeps an object for a new userdata. </summary>
    public (int Slot, int Generation) Add(object value)
    {
        if (!_freeSlots.TryPop(out int slot))
        {
            if (_used == _objects.Length)
            {
                int length = Math.Max(16, _objects.Length * 2);
                Array.Resize(ref _objects, length);
                Array.Resize(ref _generations, length);
            }
            slot = _used++;
        }
        _objects[slot] = value;
        _slotByObject[value] = slot;
        int generation = _generations[slot];
        if (_scopes.Count > 0)
        {
            if (_createdInScopes.Count >= _compactAtRecords)
                CompactScopeRecords();
            _createdInScopes.Add((slot, generation));
        }
        return (slot, generation);
    }

    /// <summary> Lets go of the object of a userdata, because Luau collected it or its scope ended. </summary>
    public void Free(int slot, int generation)
    {
        if (Get(slot, generation) is not { } value)
            return;
        // The object can have a newer userdata already, which then keeps it
        if (_slotByObject.TryGetValue(value, out int current) && current == slot)
            _slotByObject.Remove(value);
        _objects[slot] = null;
        // A scope releases objects whose userdata are still alive. Their generation must never come back, so a slot
        // that ran out of generations is not reused
        if (_generations[slot] == int.MaxValue)
            return;
        _generations[slot]++;
        _freeSlots.Push(slot);
    }

    /// <summary> Starts recording the userdata created from now on. </summary>
    /// <returns> The id of the scope, for <see cref="EndScope"/>. No other scope of the state gets it. </returns>
    public long BeginScope()
    {
        long id = ++_lastScopeId;
        _scopes.Add((id, _createdInScopes.Count));
        return id;
    }

    /// <summary> Releases the objects of the userdata created since the innermost scope started. </summary>
    /// <param name="id"> The id <see cref="BeginScope"/> returned for the scope. </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the scope is not the innermost open one: it was ended already, or a scope it encloses is open.
    /// </exception>
    public void EndScope(long id)
    {
        if (_scopes.Count == 0 || _scopes[^1].Id != id)
        {
            throw new InvalidOperationException(
                "A userdata scope must be disposed once, and after the scopes that began inside it."
            );
        }
        int start = _scopes[^1].Start;
        _scopes.RemoveAt(_scopes.Count - 1);
        for (int i = start; i < _createdInScopes.Count; i++)
            Free(_createdInScopes[i].Slot, _createdInScopes[i].Generation);
        _createdInScopes.RemoveRange(start, _createdInScopes.Count - start);
        if (_scopes.Count == 0)
            _compactAtRecords = MinimumRecordsToCompact;
    }

    // Drops the records of userdata that Luau has collected, so that a long scope keeps only those that are alive
    private void CompactScopeRecords()
    {
        int kept = 0;
        int scope = 0;
        for (int i = 0; i < _createdInScopes.Count; i++)
        {
            for (; scope < _scopes.Count && _scopes[scope].Start == i; scope++)
                _scopes[scope] = (_scopes[scope].Id, kept);
            (int slot, int generation) = _createdInScopes[i];
            if (Get(slot, generation) is not null)
                _createdInScopes[kept++] = (slot, generation);
        }
        for (; scope < _scopes.Count; scope++)
            _scopes[scope] = (_scopes[scope].Id, kept);
        _createdInScopes.RemoveRange(kept, _createdInScopes.Count - kept);
        _compactAtRecords = Math.Max(MinimumRecordsToCompact, kept * 2);
    }
}
