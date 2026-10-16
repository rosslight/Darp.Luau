using System.Diagnostics;

namespace Darp.Luau.Utils;

/// <summary> The managed objects of the userdata of one state, in slots that the userdata refer to. </summary>
/// <remarks>
/// Only the thread that uses the state reads and writes it, also from the destructors of userdata. A slot is reused
/// once its object is released, with a new generation, so a userdata of the released object never sees the next one.
/// </remarks>
internal sealed class UserdataObjects
{
    // A guess at how many userdata a state that creates any keeps alive at first. The slots double when they run out.
    private const int InitialSlots = 16;

    private object?[] _objects = new object?[InitialSlots];
    private int[] _generations = new int[InitialSlots];
    private readonly Stack<int> _freeSlots = [];
    private readonly Dictionary<object, int> _slotByObject = new(ReferenceEqualityComparer.Instance);
    private int _used;

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
                Array.Resize(ref _objects, _objects.Length * 2);
                Array.Resize(ref _generations, _generations.Length * 2);
            }
            slot = _used++;
        }
        _objects[slot] = value;
        _slotByObject[value] = slot;
        return (slot, _generations[slot]);
    }

    /// <summary> Lets go of the object of a userdata, because Luau collected it. </summary>
    public void Free(int slot, int generation)
    {
        object? value = Get(slot, generation);
        Debug.Assert(value is not null, "Luau collects a userdata once, so its slot still holds its object.");
        // The object can have a newer userdata already, which then keeps it
        if (_slotByObject.TryGetValue(value, out int current) && current == slot)
            _slotByObject.Remove(value);
        _objects[slot] = null;
        _generations[slot]++;
        _freeSlots.Push(slot);
    }
}
