using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using Darp.Luau.Native;
using Darp.Luau.Utils;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Internal;

/// <summary> Reads and writes table values like a script does: with the metamethods of the table. </summary>
/// <remarks>
/// Luau decides what an access does and whether it fails. Key and value are pushed once and <see cref="LuauVm"/>
/// returns the outcome, so this class only moves values on the stack.
/// </remarks>
internal static unsafe class LuauTableAccessCore
{
    internal static void Set<T>(scoped in T source, in IntoLuau key, in IntoLuau value)
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
        LuaException.ThrowIfNotOk(L, LuauVm.SetTable(state, L, -3), "lua_settable");
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
    /// <remarks> Not generic, and the getters call it directly: one call between them and Luau. </remarks>
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
        int result = LuauVm.GetTable(state, L, -2); // [table, value] or [table, error]
        if (result >= 0)
        {
            actualType = (lua_Type)result;
            error = null;
            return true;
        }

        nuint length = 0;
        byte* message = lua_tolstring(L, -1, &length);
        error = message is null ? "<unknown lua error>" : Encoding.UTF8.GetString(message, (int)length);
        lua_pop(L, 1);
        actualType = lua_Type.LUA_TNIL;
        return false;
    }
}
