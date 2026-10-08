using System.Diagnostics;
using Darp.Luau.Native;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Utils;

internal static class LuauNativeMethods
{
    public static unsafe int luaL_ref(lua_State* L, int t)
    {
        // Luau lua_ref behaves differently from normal lua!
        // See https://github.com/luau-lang/luau/issues/247#issuecomment-983043114
        Debug.Assert(t == LUA_REGISTRYINDEX);
        int r = lua_ref(L, -1);
        lua_pop(L, 1);
        return r;
    }
}
