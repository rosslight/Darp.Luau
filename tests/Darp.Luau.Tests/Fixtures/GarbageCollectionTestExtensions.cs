using Darp.Luau.Native;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Tests;

internal static class GarbageCollectionTestExtensions
{
    /// <summary> Runs a full Luau garbage collection. A script cannot: Luau gives it no <c>collectgarbage("collect")</c>. </summary>
    public static unsafe void CollectGarbage(this LuauState state) =>
        _ = lua_gc(state.L, (int)lua_GCOp.LUA_GCCOLLECT, 0);

    /// <summary> Gets the memory Luau has allocated for the state, in kilobytes. </summary>
    public static unsafe int GetLuauKilobytes(this LuauState state) => lua_gc(state.L, (int)lua_GCOp.LUA_GCCOUNT, 0);
}
