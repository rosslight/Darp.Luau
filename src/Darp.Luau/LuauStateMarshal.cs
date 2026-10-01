using System.Runtime.CompilerServices;
using System.Text;
using Darp.Luau.Native;
using Darp.Luau.Utils;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau;

/// <summary>
/// Provides unsafe helpers for interacting directly with <see cref="lua_State"/>.
/// </summary>
internal static class LuauStateMarshal
{
    public static unsafe int ReturnResult(lua_State* state, LuaResult<int, string> result)
    {
        return result.TryGetValue(out int value, out string? error)
            ? ReturnSuccess(state, value)
            : ReturnError(state, error);
    }

    public static unsafe int ReturnError(lua_State* state, ReadOnlySpan<byte> message)
    {
        if (message.IsEmpty)
            message = "something went wrong"u8;
        PushString(state, message);
        return -1;
    }

    public static unsafe int ReturnError(lua_State* state, ReadOnlySpan<char> message)
    {
        if (message.IsEmpty)
            message = "something went wrong";
        PushString(state, message);
        return -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe int ReturnCallbackException(lua_State* state, string callbackName, Exception exception)
    {
        string callbackError = $"{callbackName} callback failed: {exception.GetType().Name}: {exception.Message}";
        return ReturnError(state, callbackError);
    }

    public static unsafe int ReturnAsyncCallbackResultMarker(LuauState state, lua_State* luaState, LuauReturn result)
    {
        int markerIndex = lua_gettop(luaState) + 1;
        lua_pushboolean(luaState, 1);
        try
        {
            if (!result.TryPushValues(state, luaState, out int outputCount, out string? error))
            {
                lua_settop(luaState, markerIndex - 1);
                return ReturnAsyncCallbackErrorMarker(luaState, error);
            }

            lua_pushinteger(luaState, outputCount);
            lua_insert(luaState, markerIndex + 1);
            return outputCount + 2;
        }
        catch (Exception exception)
        {
            lua_settop(luaState, markerIndex - 1);
            return ReturnAsyncCallbackErrorMarker(luaState, $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    public static unsafe int ReturnAsyncCallbackErrorMarker(lua_State* luaState, string? error)
    {
        lua_pushboolean(luaState, 0);
        PushString(luaState, error);
        return 2;
    }

    public static unsafe int ReturnSuccess(lua_State* state, int outputCount)
    {
        if (outputCount < 0)
            throw new ArgumentOutOfRangeException(
                nameof(outputCount),
                outputCount,
                "Output count cannot be negative."
            );
        _ = state;
        return outputCount;
    }

    public static unsafe bool TryGetString(lua_State* L, int stackIndex, out ReadOnlySpan<byte> value)
    {
        if ((lua_Type)lua_type(L, stackIndex) is not lua_Type.LUA_TSTRING)
        {
            value = default;
            return false;
        }

        nuint keyLength;
        byte* keyUtf8 = lua_tolstring(L, stackIndex, &keyLength);
        if (keyUtf8 is null)
        {
            value = default;
            return false;
        }
        value = new ReadOnlySpan<byte>(keyUtf8, checked((int)keyLength));
        return true;
    }

    public static unsafe bool TryGetNameCall(lua_State* L, out ReadOnlySpan<byte> methodName)
    {
        int atom = 0;
        byte* name = lua_namecallatom(L, &atom);
        if (name is null)
        {
            methodName = default;
            return false;
        }

        int length = 0;
        while (name[length] != 0)
            length++;

        methodName = new ReadOnlySpan<byte>(name, length);
        return true;
    }

    public static unsafe void PushString(lua_State* state, string? message)
    {
        if (message is null)
        {
            lua_pushnil(state);
            return;
        }
        PushString(state, message.AsSpan());
    }

    public static unsafe void PushString(lua_State* state, ReadOnlySpan<char> message)
    {
        Span<byte> utf8Message = stackalloc byte[Encoding.UTF8.GetMaxByteCount(message.Length)];
        int utf8Length = Encoding.UTF8.GetBytes(message, utf8Message);
        fixed (byte* pMessage = utf8Message)
        {
            lua_pushlstring(state, pMessage, (nuint)utf8Length);
        }
    }

    public static unsafe void PushString(lua_State* state, ReadOnlySpan<byte> message)
    {
        fixed (byte* pMessage = message)
        {
            lua_pushlstring(state, pMessage, (nuint)message.Length);
        }
    }
}
