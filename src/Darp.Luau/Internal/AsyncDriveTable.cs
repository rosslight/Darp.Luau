using Darp.Luau.Native;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Internal;

/// <summary>
/// The async drives in progress on one <see cref="LuauState"/>: what a managed callback needs to know about the
/// host call that runs its coroutine.
/// </summary>
/// <remarks>
/// A drive occupies a slot for as long as it runs and stores it in the thread data of its coroutine. Slots are
/// reused, so a drive allocates nothing. Like the rest of the state, the table is only used by one thread at a time.
/// </remarks>
internal sealed class AsyncDriveTable
{
    private const int NoSlot = -1;

    // Empty until the first async drive, so that a state without async calls does not pay for the slots.
    private Slot[] _slots = [];
    private int _usedSlotCount;
    private int _firstFreeSlot = NoSlot;

    /// <summary> Registers a drive of <paramref name="coroutine"/>. </summary>
    /// <returns>The slot to pass to <see cref="Remove"/> when the drive ends.</returns>
    public unsafe int Add(lua_State* coroutine, CancellationToken cancellationToken)
    {
        int slot = _firstFreeSlot;
        if (slot != NoSlot)
        {
            _firstFreeSlot = _slots[slot].NextFreeSlot;
        }
        else
        {
            if (_usedSlotCount == _slots.Length)
                Array.Resize(ref _slots, Math.Max(4, _slots.Length * 2));
            slot = _usedSlotCount++;
        }
        _slots[slot] = new Slot { CancellationToken = cancellationToken };
        // Offset by one: thread data of zero means that no async drive runs the coroutine.
        lua_setthreaddata(coroutine, (void*)(nint)(slot + 1));
        return slot;
    }

    /// <summary> Ends the drive in <paramref name="slot"/>. </summary>
    /// <param name="slot">The slot returned by <see cref="Add"/>.</param>
    /// <param name="coroutine">The driven coroutine, or <c>null</c> when its state is disposed and its memory is gone.</param>
    public unsafe void Remove(int slot, lua_State* coroutine)
    {
        if (coroutine is not null)
            lua_setthreaddata(coroutine, null);
        _slots[slot] = new Slot { NextFreeSlot = _firstFreeSlot };
        _firstFreeSlot = slot;
    }

    /// <summary> The work the drive in <paramref name="slot"/> awaits, <c>null</c> while its coroutine runs. </summary>
    public Task<LuauReturn>? GetPending(int slot) => _slots[slot].Pending;

    public void SetPending(int slot, Task<LuauReturn>? pending) => _slots[slot].Pending = pending;

    /// <summary> Hands the pending work of a managed callback to the drive of <paramref name="coroutine"/>. </summary>
    /// <returns><c>false</c> when no async drive runs the coroutine.</returns>
    public unsafe bool TrySetPending(lua_State* coroutine, Task<LuauReturn> pending)
    {
        if (!TryGetSlot(coroutine, out int slot))
            return false;
        _slots[slot].Pending = pending;
        return true;
    }

    /// <summary> Whether <paramref name="coroutine"/> is suspended in a managed callback its drive awaits. </summary>
    public unsafe bool IsAwaiting(lua_State* coroutine) =>
        TryGetSlot(coroutine, out int slot) && _slots[slot].Pending is not null;

    /// <summary> The token of the async host call driving <paramref name="coroutine"/>, if any. </summary>
    public unsafe CancellationToken GetCancellationToken(lua_State* coroutine) =>
        TryGetSlot(coroutine, out int slot) ? _slots[slot].CancellationToken : CancellationToken.None;

    private static unsafe bool TryGetSlot(lua_State* coroutine, out int slot)
    {
        slot = (int)(nint)lua_getthreaddata(coroutine) - 1;
        return slot != NoSlot;
    }

    private struct Slot
    {
        public CancellationToken CancellationToken;
        public Task<LuauReturn>? Pending;

        // Links the free slots.
        public int NextFreeSlot;
    }
}
