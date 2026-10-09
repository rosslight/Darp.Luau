using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Internal;
using Darp.Luau.Native;
using Darp.Luau.Utils;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau;

public unsafe partial struct LuauTable
{
    private bool TryGetNumber(in IntoLuau key, out double value, [NotNullWhen(false)] out string? error)
    {
#if DEBUG
        using var guard = new StackGuard(_state!.L, expectedDelta: 0);
#endif
        value = 0;
        if (!TryGetRequired(key, lua_Type.LUA_TNUMBER, out lua_State* L, out error))
            return false;
        value = lua_tonumber(L, -1);
        lua_pop(L, 2);
        return true;
    }

    private bool TryGetNumberOrNil(in IntoLuau key, out double? value, [NotNullWhen(false)] out string? error)
    {
#if DEBUG
        using var guard = new StackGuard(_state!.L, expectedDelta: 0);
#endif
        value = null;
        if (!TryGetOptional(key, lua_Type.LUA_TNUMBER, out lua_State* L, out bool isNil, out error))
            return false;
        if (isNil)
            return true;

        value = lua_tonumber(L, -1);
        lua_pop(L, 2);
        return true;
    }

    private bool TryGetBoolean(in IntoLuau key, out bool value, [NotNullWhen(false)] out string? error)
    {
#if DEBUG
        using var guard = new StackGuard(_state!.L, expectedDelta: 0);
#endif
        value = false;
        if (!TryGetRequired(key, lua_Type.LUA_TBOOLEAN, out lua_State* L, out error))
            return false;
        value = lua_toboolean(L, -1) == 1;
        lua_pop(L, 2);
        return true;
    }

    private bool TryGetBooleanOrNil(in IntoLuau key, out bool? value, [NotNullWhen(false)] out string? error)
    {
#if DEBUG
        using var guard = new StackGuard(_state!.L, expectedDelta: 0);
#endif
        value = null;
        if (!TryGetOptional(key, lua_Type.LUA_TBOOLEAN, out lua_State* L, out bool isNil, out error))
            return false;
        if (isNil)
            return true;

        value = lua_toboolean(L, -1) == 1;
        lua_pop(L, 2);
        return true;
    }

    internal bool TryGetUtf8String(
        scoped in IntoLuau key,
        out ReadOnlySpan<byte> value,
        [NotNullWhen(false)] out string? error
    )
    {
#if DEBUG
        using var guard = new StackGuard(_state!.L, expectedDelta: 0);
#endif
        value = default;
        if (!TryGetRequired(key, lua_Type.LUA_TSTRING, out lua_State* L, out error))
            return false;
        nuint len = 0;
        byte* pStr = lua_tolstring(L, -1, &len);
        if (pStr is null)
        {
            error = "Table value returned a null string pointer.";
            lua_pop(L, 2);
            return false;
        }
        value = new ReadOnlySpan<byte>(pStr, checked((int)len));
        lua_pop(L, 2);
        return true;
    }

    internal bool TryGetUtf8StringOrNil(
        IntoLuau key,
        out ReadOnlySpan<byte> value,
        out bool isNil,
        [NotNullWhen(false)] out string? error
    )
    {
#if DEBUG
        using var guard = new StackGuard(_state!.L, expectedDelta: 0);
#endif
        value = default;
        if (!TryGetOptional(key, lua_Type.LUA_TSTRING, out lua_State* L, out isNil, out error))
            return false;
        if (isNil)
            return true;

        nuint len = 0;
        byte* pStr = lua_tolstring(L, -1, &len);
        if (pStr is null)
        {
            error = "Table value returned a null string pointer.";
            lua_pop(L, 2);
            return false;
        }

        value = new ReadOnlySpan<byte>(pStr, checked((int)len));
        lua_pop(L, 2);
        return true;
    }

    internal bool TryGetBuffer(
        scoped in IntoLuau key,
        out ReadOnlySpan<byte> value,
        [NotNullWhen(false)] out string? error
    )
    {
#if DEBUG
        using var guard = new StackGuard(_state!.L, expectedDelta: 0);
#endif
        value = default;
        if (!TryGetRequired(key, lua_Type.LUA_TBUFFER, out lua_State* L, out error))
            return false;

        nuint length = 0;
        void* pBuf = lua_tobuffer(L, -1, &length);
        if (pBuf is null)
        {
            error = "Table value returned a null buffer pointer.";
            lua_pop(L, 2);
            return false;
        }

        value = new ReadOnlySpan<byte>(pBuf, checked((int)length));
        lua_pop(L, 2);
        return true;
    }

    internal bool TryGetBufferOrNil(
        IntoLuau key,
        out ReadOnlySpan<byte> value,
        out bool isNil,
        [NotNullWhen(false)] out string? error
    )
    {
#if DEBUG
        using var guard = new StackGuard(_state!.L, expectedDelta: 0);
#endif
        value = default;
        if (!TryGetOptional(key, lua_Type.LUA_TBUFFER, out lua_State* L, out isNil, out error))
            return false;
        if (isNil)
            return true;

        nuint length = 0;
        void* pBuf = lua_tobuffer(L, -1, &length);
        if (pBuf is null)
        {
            error = "Table value returned a null buffer pointer.";
            lua_pop(L, 2);
            return false;
        }

        value = new ReadOnlySpan<byte>(pBuf, checked((int)length));
        lua_pop(L, 2);
        return true;
    }

    private bool TryGetUserdata<T>(
        IntoLuau key,
        [NotNullWhen(true)] out T? value,
        [NotNullWhen(false)] out string? error
    )
        where T : class, ILuauUserdata<T>
    {
#if DEBUG
        using var guard = new StackGuard(_state!.L, expectedDelta: 0);
#endif
        value = null;
        if (!TryGetRequired(key, lua_Type.LUA_TUSERDATA, out lua_State* L, out error))
            return false;

        bool ok = ManagedUserdataResolver.TryResolve(L, -1, out value, out error, valueLabel: "Table value");
        lua_pop(L, 2);
        return ok;
    }

    private bool TryGetUserdataOrNil<T>(in IntoLuau key, out T? value, [NotNullWhen(false)] out string? error)
        where T : class, ILuauUserdata<T>
    {
#if DEBUG
        using var guard = new StackGuard(_state!.L, expectedDelta: 0);
#endif
        value = null;
        if (!TryGetOptional(key, lua_Type.LUA_TUSERDATA, out lua_State* L, out bool isNil, out error))
            return false;
        if (isNil)
            return true;

        bool ok = ManagedUserdataResolver.TryResolve(L, -1, out value, out error, valueLabel: "Table value");
        lua_pop(L, 2);
        return ok;
    }

    private bool TryGetLuauValue(in IntoLuau key, out LuauValue value, [NotNullWhen(false)] out string? error)
    {
        _state.ThrowIfDisposed();
#if DEBUG
        using var guard = new StackGuard(_state.L, expectedDelta: 0);
#endif
        value = default;
        RegistryReferenceTracker.TrackedReference reference = _state.GetTrackedReferenceOrThrow(_handle);
        if (!LuauTableAccessCore.TryGet(reference, key, out lua_State* L, out _, out error))
            return false;

        value = LuauValue.ToValue(_state);
        lua_pop(L, 2);
        return true;
    }

    private bool TryGetLuauTable(in IntoLuau key, out LuauTable value, [NotNullWhen(false)] out string? error)
    {
        _state.ThrowIfDisposed();
#if DEBUG
        using var guard = new StackGuard(_state!.L, expectedDelta: 0);
#endif
        value = default;
        if (!TryGetRequired(key, lua_Type.LUA_TTABLE, out lua_State* L, out error))
            return false;

        value = new LuauTable(_state, _state.ReferenceTracker.TrackRef(L, -1));
        lua_pop(L, 2);
        return true;
    }

    private bool TryGetLuauFunction(in IntoLuau key, out LuauFunction value, [NotNullWhen(false)] out string? error)
    {
        _state.ThrowIfDisposed();
#if DEBUG
        using var guard = new StackGuard(_state.L, expectedDelta: 0);
#endif
        value = default;
        if (!TryGetRequired(key, lua_Type.LUA_TFUNCTION, out lua_State* L, out error))
            return false;

        value = new LuauFunction(_state, _state.ReferenceTracker.TrackRef(L, -1));
        lua_pop(L, 2);
        return true;
    }

    private bool TryGetLuauCoroutine(in IntoLuau key, out LuauCoroutine value, [NotNullWhen(false)] out string? error)
    {
        _state.ThrowIfDisposed();
#if DEBUG
        using var guard = new StackGuard(_state.L, expectedDelta: 0);
#endif
        value = default;
        if (!TryGetRequired(key, lua_Type.LUA_TTHREAD, out lua_State* L, out error))
            return false;

        value = new LuauCoroutine(_state, _state.ReferenceTracker.TrackRef(L, -1));
        lua_pop(L, 2);
        return true;
    }

    private bool TryGetLuauString(in IntoLuau key, out LuauString value, [NotNullWhen(false)] out string? error)
    {
        _state.ThrowIfDisposed();
#if DEBUG
        using var guard = new StackGuard(_state.L, expectedDelta: 0);
#endif
        value = default;
        if (!TryGetRequired(key, lua_Type.LUA_TSTRING, out lua_State* L, out error))
            return false;

        value = new LuauString(_state, _state.ReferenceTracker.TrackRef(L, -1));
        lua_pop(L, 2);
        return true;
    }

    private bool TryGetLuauBuffer(in IntoLuau key, out LuauBuffer value, [NotNullWhen(false)] out string? error)
    {
        _state.ThrowIfDisposed();
#if DEBUG
        using var guard = new StackGuard(_state.L, expectedDelta: 0);
#endif
        value = default;
        if (!TryGetRequired(key, lua_Type.LUA_TBUFFER, out lua_State* L, out error))
            return false;

        ulong handle = _state.ReferenceTracker.TrackRef(L, -1);
        value = new LuauBuffer(_state, handle);
        lua_pop(L, 2);
        return true;
    }

    private bool TryGetLuauUserdata(in IntoLuau key, out LuauUserdata value, [NotNullWhen(false)] out string? error)
    {
        _state.ThrowIfDisposed();
#if DEBUG
        using var guard = new StackGuard(_state.L, expectedDelta: 0);
#endif
        value = default;
        if (!TryGetRequired(key, lua_Type.LUA_TUSERDATA, out lua_State* L, out error))
            return false;

        ulong handle = _state.ReferenceTracker.TrackRef(L, -1);
        value = new LuauUserdata(_state, handle);
        lua_pop(L, 2);
        return true;
    }

    private bool TryGetRequired(
        in IntoLuau key,
        lua_Type expectedType,
        out lua_State* L,
        [NotNullWhen(false)] out string? error
    )
    {
        L = null;
        error = null;
        return _state.TryGetTrackedReference(_handle, out RegistryReferenceTracker.TrackedReference? reference)
            && LuauTableAccessCore.TryGetRequired(reference, key, expectedType, out L, out error);
    }

    private bool TryGetOptional(
        in IntoLuau key,
        lua_Type expectedType,
        out lua_State* L,
        out bool isNil,
        [NotNullWhen(false)] out string? error
    )
    {
        L = null;
        isNil = false;
        error = null;
        return _state.TryGetTrackedReference(_handle, out RegistryReferenceTracker.TrackedReference? reference)
            && LuauTableAccessCore.TryGetOptional(reference, key, expectedType, out L, out isNil, out error);
    }

    private static LuaGetException CreateReadException(string error) => new(error);
}
