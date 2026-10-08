using System.Diagnostics;
using System.Runtime.InteropServices;
using Darp.Luau.Native;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Utils;

internal static class LuauNativeMethods
{
    // Every host write to a table asks these two questions first. Luau answers each from a field or two, without
    // allocating, raising an error or calling back, so the calls skip the GC transition of a regular P/Invoke. They
    // are resolved from the library the bindings loaded. Where that is not possible, the bindings are called.
    private static readonly unsafe delegate* unmanaged[Cdecl, SuppressGCTransition]<lua_State*, int, int> GetReadOnly =
        (delegate* unmanaged[Cdecl, SuppressGCTransition]<lua_State*, int, int>)GetExport("lua_getreadonly");

    private static readonly unsafe delegate* unmanaged[Cdecl, SuppressGCTransition]<lua_State*, int, int> GetMetatable =
        (delegate* unmanaged[Cdecl, SuppressGCTransition]<lua_State*, int, int>)GetExport("lua_getmetatable");

    /// <summary> Whether the table at <paramref name="idx"/> is frozen. </summary>
    public static unsafe bool IsReadOnly(lua_State* L, int idx) =>
        (GetReadOnly is not null ? GetReadOnly(L, idx) : lua_getreadonly(L, idx)) != 0;

    /// <summary> Pushes the metatable of the value at <paramref name="idx"/>, if it has one. </summary>
    public static unsafe bool TryPushMetatable(lua_State* L, int idx) =>
        (GetMetatable is not null ? GetMetatable(L, idx) : lua_getmetatable(L, idx)) != 0;

    private static nint GetExport(string name) =>
        NativeLibrary.TryLoad("luau", typeof(LuauNative).Assembly, searchPath: null, out nint library)
        && NativeLibrary.TryGetExport(library, name, out nint export)
            ? export
            : 0;

    public static unsafe int luaL_ref(lua_State* L, int t)
    {
        // Luau lua_ref behaves differently from normal lua!
        // See https://github.com/luau-lang/luau/issues/247#issuecomment-983043114
        Debug.Assert(t == LUA_REGISTRYINDEX);
        int r = lua_ref(L, -1);
        lua_pop(L, 1);
        return r;
    }

    public static unsafe void CompileLoadAndCall(
        lua_State* L,
        ReadOnlySpan<byte> source,
        ReadOnlySpan<byte> chunkName,
        int nResults
    )
    {
        fixed (byte* pSource = source)
        fixed (byte* pChunkName = chunkName)
        {
            nuint resultSize = 0;
            byte* pByteCode = luau_compile(pSource, (nuint)source.Length, null, &resultSize);
            try
            {
                int loadStatus = luau_load(L, pChunkName, pByteCode, resultSize, 0);
                LuaException.ThrowIfNotOk(L, loadStatus, "luau_load");

                int callStatus = lua_pcall(L, 0, nResults, 0);
                LuaException.ThrowIfNotOk(L, callStatus, "lua_pcall");
            }
            finally
            {
                luau_free(pByteCode);
            }
        }
    }
}
