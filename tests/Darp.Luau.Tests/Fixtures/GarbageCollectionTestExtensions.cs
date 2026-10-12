using Darp.Luau.Native;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Tests;

internal static class GarbageCollectionTestExtensions
{
    /// <summary> Runs a full Luau garbage collection. A script cannot: Luau gives it no <c>collectgarbage("collect")</c>. </summary>
    public static unsafe void CollectGarbage(this LuauState state) =>
        _ = lua_gc(state.L, (int)lua_GCOp.LUA_GCCOLLECT, 0);

    /// <summary> Gets the number of values on the stack of the state. A host call leaves it as it found it. </summary>
    public static unsafe int GetStackTop(this LuauState state) => lua_gettop(state.L);

    /// <summary> Gets the memory Luau has allocated for the state, in kilobytes. </summary>
    public static unsafe int GetLuauKilobytes(this LuauState state) => lua_gc(state.L, (int)lua_GCOp.LUA_GCCOUNT, 0);
}
