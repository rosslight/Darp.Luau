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
        // The coroutine may have been taken from the drive and may be run by another one by now.
        if (coroutine is not null && TryGetSlot(coroutine, out int currentSlot) && currentSlot == slot)
            lua_setthreaddata(coroutine, null);
        _slots[slot] = new Slot { NextFreeSlot = _firstFreeSlot };
        _firstFreeSlot = slot;
    }

    /// <summary> The work the drive in <paramref name="slot"/> awaits, none while its coroutine runs. </summary>
    public PendingWork GetPending(int slot) => _slots[slot].Pending;

    public void ClearPending(int slot) => _slots[slot].Pending = default;

    /// <summary> Hands the pending work of a managed callback to the drive of <paramref name="coroutine"/>. </summary>
    /// <returns><c>false</c> when no async drive runs the coroutine.</returns>
    public unsafe bool TrySetPending(lua_State* coroutine, PendingWork pending)
    {
        if (!TryGetSlot(coroutine, out int slot))
            return false;
        _slots[slot].Pending = pending;
        return true;
    }

    /// <summary> Whether an async drive runs <paramref name="coroutine"/>. </summary>
    public static unsafe bool IsDriving(lua_State* coroutine) => TryGetSlot(coroutine, out _);

    /// <summary> Whether the host cancelled the call of the drive in <paramref name="slot"/>. </summary>
    public bool IsCancellationRequested(int slot) => _slots[slot].CancellationToken.IsCancellationRequested;

    /// <summary> Whether <paramref name="coroutine"/> is suspended in a managed callback its drive awaits. </summary>
    public unsafe bool IsAwaiting(lua_State* coroutine) =>
        TryGetSlot(coroutine, out int slot) && !_slots[slot].Pending.IsNone;

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
        public PendingWork Pending;

        // Links the free slots.
        public int NextFreeSlot;
    }
}
