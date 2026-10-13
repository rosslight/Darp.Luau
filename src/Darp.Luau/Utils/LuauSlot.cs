using Darp.Luau.Native;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Utils;

/// <summary> Where a Luau value is: a stack slot, or a tracked registry reference. </summary>
/// <remarks>
/// A borrowed view holds one and so does not care which of the two it is a view of: a callback argument is a stack
/// slot, an owned reference a registry reference. An operation copies the value to the top of the main stack of
/// the state with <see cref="Push"/>, works there and pops it.
/// </remarks>
internal readonly unsafe ref struct LuauSlot
{
    private readonly LuauState _state;

    // A stack slot. Null for a registry reference.
    private readonly lua_State* _luaState;
    private readonly int _stackIndex;

    public LuauSlot(LuauState state, lua_State* luaState, int stackIndex)
    {
        ArgumentNullException.ThrowIfNull(luaState);
        if (!state.OwnsThread(luaState))
            throw new InvalidOperationException("Cross-state stack reference usage is not allowed.");

        _state = state;
        _luaState = luaState;
        _stackIndex = stackIndex;
    }

    public LuauSlot(LuauState state, RegistryReferenceTracker.TrackedReference reference)
    {
        _state = state;
        Reference = reference;
    }

    /// <summary> The registry reference, or null for a stack slot. </summary>
    public RegistryReferenceTracker.TrackedReference? Reference { get; }

    /// <summary> The state the value belongs to. </summary>
    /// <exception cref="ObjectDisposedException">Thrown when the state is disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown for a slot that was never set, like that of <c>default</c>.</exception>
    public LuauState State
    {
        get
        {
            _state.ThrowIfDisposed();
            return _state;
        }
    }

    /// <summary> Copies the value to the top of the main stack of <paramref name="state"/>. </summary>
    /// <param name="state">The state of the slot, which the caller got from <see cref="State"/>.</param>
    /// <exception cref="ObjectDisposedException">Thrown when the registry reference was released.</exception>
    public void Push(LuauState state)
    {
        lua_State* L = state.L;
        if (Reference is { } reference)
        {
            reference.Push(L);
            return;
        }

        lua_pushvalue(_luaState, _stackIndex);
        if (_luaState != L)
            lua_xmove(_luaState, L, 1);
    }

    /// <summary> Tracks the value under a handle of its own, for an owned reference. </summary>
    public ulong ToOwnedHandle()
    {
        LuauState state = State;
        Push(state);
        return state.ReferenceTracker.TrackAndPopRef(state.L, -1);
    }

    /// <summary> Describes the value like Luau's <c>tostring</c> does without metamethods. </summary>
    public override string ToString()
    {
        LuauState state = State;
        Push(state);
        string text = Helpers.StackString(state, -1);
        lua_pop(state.L, 1);
        return text;
    }
}
