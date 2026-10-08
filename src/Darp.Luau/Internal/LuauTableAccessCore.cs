using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Darp.Luau.Native;
using Darp.Luau.Utils;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Internal;

/// <summary> Reads and writes table values like a script does: with the metamethods of the table. </summary>
/// <remarks>
/// A table without <c>__index</c> or <c>__newindex</c> is accessed raw, which cannot raise a Luau error. Any other
/// table goes through <see cref="ProtectedTableAccess"/>, because an error raised by a metamethod must not unwind
/// through managed frames.
/// </remarks>
internal static unsafe class LuauTableAccessCore
{
    internal static void Set<T>(scoped in T source, in IntoLuau key, in IntoLuau value)
        where T : IReferenceSource, allows ref struct
    {
        LuauState state = source.Validate();
        if (key.Type is IntoLuau.Kind.Nil)
            throw new ArgumentNullException(nameof(key), "Cannot set a table value with nil key");
        if (key.IsNaN)
            throw new ArgumentException("Cannot set a table value with a NaN key", nameof(key));
        lua_State* L = state.L;
#if DEBUG
        using var guard = new StackGuard(L, expectedDelta: 0);
#endif
        using PopDisposable _ = source.PushToTop(); // [table]
        if (LuauNativeMethods.IsReadOnly(L, -1))
            throw new LuaException("Could not set the table value: attempt to modify a readonly table");

        if (HasMetamethod(L, -1, "__newindex\0"u8))
        {
            if (!state.ProtectedTableAccess.TrySet(state, L, key, value, out string? error))
                throw new LuaException($"Could not set the table value: {error}");
            return;
        }

        key.Push(state); // [table, key]
        try
        {
            value.Push(state); // [table, key, value]
        }
        catch
        {
            lua_pop(L, 1);
            throw;
        }
        lua_rawset(L, -3);
    }

    internal static bool ContainsKey<T>(scoped in T source, in IntoLuau key)
        where T : IReferenceSource, allows ref struct
    {
        LuauState state = source.Validate();
        if (key.Type is IntoLuau.Kind.Nil)
            throw new ArgumentNullException(nameof(key), "Cannot set a table value with nil key");
        lua_State* L = state.L;
#if DEBUG
        using var guard = new StackGuard(L, expectedDelta: 0);
#endif
        using PopDisposable _ = source.PushToTop(); // [table]
        if (!TryPushValue(state, L, key, out lua_Type actualType, out string? error))
            throw new LuaException($"Could not get the table value: {error}");
        lua_pop(L, 1);
        return actualType != lua_Type.LUA_TNIL;
    }

    internal static int ListCount<T>(scoped in T source)
        where T : IReferenceSource, allows ref struct
    {
        LuauState state = source.Validate();
        lua_State* L = state.L;
#if DEBUG
        using var guard = new StackGuard(L, expectedDelta: 0);
#endif

        using PopDisposable _ = source.PushToStack(out int stackIndex);
        return lua_objlen(L, stackIndex);
    }

    /// <summary> Pushes the table and the value at <paramref name="key"/>. Pushes nothing when it fails. </summary>
    public static bool TryGet<T>(
        scoped in T source,
        in IntoLuau key,
        out lua_State* L,
        out lua_Type actualType,
        [NotNullWhen(false)] out string? error
    )
        where T : IReferenceSource, allows ref struct
    {
        LuauState state = source.Validate();
        L = state.L;
#pragma warning disable CA2000 // The table stays on the stack below the value. The caller pops both.
        _ = source.PushToTop(); // [table]
#pragma warning restore CA2000
        return TryPushValueOrPopTable(state, L, key, out actualType, out error);
    }

    /// <summary> Pushes <c>table[key]</c> for the table on top of the stack. Pops the table when it fails. </summary>
    private static bool TryPushValueOrPopTable(
        LuauState state,
        lua_State* L,
        in IntoLuau key,
        out lua_Type actualType,
        [NotNullWhen(false)] out string? error
    )
    {
        try
        {
            if (TryPushValue(state, L, key, out actualType, out error))
                return true;
        }
        catch
        {
            lua_pop(L, 1);
            throw;
        }
        lua_pop(L, 1);
        return false;
    }

    /// <inheritdoc cref="TryGet{T}"/>
    /// <remarks> Fails when the value is not of <paramref name="expectedType"/>. </remarks>
    public static bool TryGetRequired<T>(
        scoped in T source,
        in IntoLuau key,
        lua_Type expectedType,
        out lua_State* L,
        [NotNullWhen(false)] out string? error
    )
        where T : IReferenceSource, allows ref struct
    {
        LuauState state = source.Validate();
        L = state.L;
#pragma warning disable CA2000 // The table stays on the stack below the value. The caller pops both.
        _ = source.PushToTop(); // [table]
#pragma warning restore CA2000
        if (!TryPushValueOrPopTable(state, L, key, out lua_Type actualType, out error))
            return false;
        if (actualType == expectedType)
            return true;

        error =
            actualType == lua_Type.LUA_TNIL
                ? $"Table value is nil but {expectedType} is required."
                : $"Table value must be {expectedType} but was {actualType}.";
        lua_pop(L, 2);
        return false;
    }

    /// <inheritdoc cref="TryGet{T}"/>
    /// <remarks> Pushes nothing when the value is <c>nil</c>. Fails when it is of another type than <paramref name="expectedType"/>. </remarks>
    public static bool TryGetOptional<T>(
        scoped in T source,
        in IntoLuau key,
        lua_Type expectedType,
        out lua_State* L,
        out bool isNil,
        [NotNullWhen(false)] out string? error
    )
        where T : IReferenceSource, allows ref struct
    {
        isNil = false;
        LuauState state = source.Validate();
        L = state.L;
#pragma warning disable CA2000 // The table stays on the stack below the value. The caller pops both.
        _ = source.PushToTop(); // [table]
#pragma warning restore CA2000
        if (!TryPushValueOrPopTable(state, L, key, out lua_Type actualType, out error))
            return false;

        if (actualType == lua_Type.LUA_TNIL)
        {
            isNil = true;
            lua_pop(L, 2);
            return true;
        }

        if (actualType == expectedType)
            return true;

        error = $"Table value must be {expectedType} or {lua_Type.LUA_TNIL} but was {actualType}.";
        lua_pop(L, 2);
        return false;
    }

    /// <summary> Pushes <c>table[key]</c> for the table on top of the stack. Pushes nothing when it fails. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryPushValue(
        LuauState state,
        lua_State* L,
        in IntoLuau key,
        out lua_Type actualType,
        [NotNullWhen(false)] out string? error
    )
    {
        key.Push(state); // [table, key]
        actualType = (lua_Type)lua_rawget(L, -2); // [table, value]
        error = null;
        if (actualType is not lua_Type.LUA_TNIL)
            return true;

        // Only a missing value is looked up with __index.
        if (!HasMetamethod(L, -2, "__index\0"u8))
            return true;
        lua_pop(L, 1); // [table]
        return state.ProtectedTableAccess.TryGet(state, L, key, out actualType, out error);
    }

    /// <summary> Whether the table at <paramref name="tableIndex"/> has the metamethod <paramref name="name"/>. </summary>
    private static bool HasMetamethod(lua_State* L, int tableIndex, ReadOnlySpan<byte> name)
    {
        if (!LuauNativeMethods.TryPushMetatable(L, tableIndex))
            return false;
        fixed (byte* pName = name)
        {
            bool hasMetamethod = (lua_Type)lua_rawgetfield(L, -1, pName) is not lua_Type.LUA_TNIL;
            lua_pop(L, 2);
            return hasMetamethod;
        }
    }
}
