using System.Diagnostics.CodeAnalysis;
using System.Text;
using Darp.Luau.Native;
using Darp.Luau.Utils;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Internal;

/// <summary>
/// Reads and writes table values through Luau functions, for tables whose metamethods run script code.
/// </summary>
/// <remarks>
/// An error raised by <c>__index</c> or <c>__newindex</c> must not unwind through managed frames. The functions run
/// under <c>lua_pcall</c>, which returns the error instead. They are compiled when a table first needs them.
/// </remarks>
internal sealed unsafe class ProtectedTableAccess
{
    private readonly int _getter;
    private readonly int _setter;

    public ProtectedTableAccess(LuauState state)
    {
        lua_State* L = state.L;
        LuauNativeMethods.CompileLoadAndCall(
            L,
            "return function(t, k) return t[k] end, function(t, k, v) t[k] = v end"u8,
            "=[Darp.Luau]\0"u8,
            nResults: 2
        );
        _setter = LuauNativeMethods.luaL_ref(L, LUA_REGISTRYINDEX);
        _getter = LuauNativeMethods.luaL_ref(L, LUA_REGISTRYINDEX);
    }

    /// <summary> Pushes <c>table[key]</c> for the table on top of the stack. Pushes nothing when it fails. </summary>
    public bool TryGet(
        LuauState state,
        lua_State* L,
        in IntoLuau key,
        out lua_Type type,
        [NotNullWhen(false)] out string? error
    )
    {
        lua_getref(L, _getter); // [table, getter]
        lua_pushvalue(L, -2); // [table, getter, table]
        try
        {
            key.Push(state); // [table, getter, table, key]
        }
        catch
        {
            lua_pop(L, 2);
            throw;
        }

        if (lua_pcall(L, 2, 1, 0) == (int)lua_Status.LUA_OK) // [table, value]
        {
            type = (lua_Type)lua_type(L, -1);
            error = null;
            return true;
        }

        type = lua_Type.LUA_TNIL;
        error = PopError(L);
        return false;
    }

    /// <summary> Runs <c>table[key] = value</c> for the table on top of the stack. Leaves the stack as it is. </summary>
    public bool TrySet(
        LuauState state,
        lua_State* L,
        in IntoLuau key,
        in IntoLuau value,
        [NotNullWhen(false)] out string? error
    )
    {
        int top = lua_gettop(L);
        lua_getref(L, _setter); // [table, setter]
        lua_pushvalue(L, -2); // [table, setter, table]
        try
        {
            key.Push(state);
            value.Push(state); // [table, setter, table, key, value]
        }
        catch
        {
            lua_settop(L, top);
            throw;
        }

        if (lua_pcall(L, 3, 0, 0) == (int)lua_Status.LUA_OK) // [table]
        {
            error = null;
            return true;
        }

        error = PopError(L);
        return false;
    }

    private static string PopError(lua_State* L)
    {
        nuint length = 0;
        byte* message = lua_tolstring(L, -1, &length);
        string error = message is null ? "<unknown lua error>" : Encoding.UTF8.GetString(message, (int)length);
        lua_pop(L, 1);
        return error;
    }
}
