using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using Darp.Luau.Native;
using Darp.Luau.Utils;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Internal;

/// <summary> Reads and writes table values like a script does: with the metamethods of the table. </summary>
/// <remarks>
/// <para>
/// Luau decides what an access does and whether it fails. Key and value are pushed once and <see cref="LuauVm"/>
/// returns the outcome, so this class only moves values on the stack.
/// </para>
/// <para>
/// The native calls of the success path stand outside of <c>try</c> blocks and <c>using</c> scopes on purpose. On
/// 64-bit targets the JIT does not inline a P/Invoke that sits in a protected region and calls it through a stub
/// instead, which costs about as much as the access itself. Only pushing the key and the value can throw, so only
/// those are guarded.
/// </para>
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
#pragma warning disable CA2000 // Popped below on every path.
        _ = source.PushToTop(); // [table]
#pragma warning restore CA2000
        PushOrPop(state, L, key, pushedCount: 1); // [table, key]
        PushOrPop(state, L, value, pushedCount: 2); // [table, key, value]

        int status = LuauVm.SetTable(state, L, -3); // [table] or [table, error]
        if (status != (int)lua_Status.LUA_OK)
        {
            string error = PopErrorMessage(L);
            lua_pop(L, 1);
            throw new LuaException($"Lua invocation lua_settable failed with status {status}: {error}");
        }
        lua_pop(L, 1);
    }

    internal static bool ContainsKey<T>(scoped in T source, in IntoLuau key)
        where T : IReferenceSource, allows ref struct
    {
        if (key.Type is IntoLuau.Kind.Nil)
            throw new ArgumentNullException(nameof(key), "Cannot set a table value with nil key");
        if (!TryGet(source, key, out lua_State* L, out lua_Type actualType, out string? error))
            throw new LuaException($"Could not get the table value: {error}");
        lua_pop(L, 2);
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
    /// <remarks> Inlined into the typed getters, so that a read is one call into this class. </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        PushOrPop(state, L, key, pushedCount: 1); // [table, key]

        int result = LuauVm.GetTable(state, L, -2); // [table, value] or [table, error]
        if (result < 0)
            return FailGet(L, out actualType, out error);

        actualType = (lua_Type)result;
        error = null;
        return true;
    }

    /// <summary> Turns the error object above the table into the outcome of a failed read and pops both. </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool FailGet(lua_State* L, out lua_Type actualType, out string error)
    {
        error = PopErrorMessage(L);
        lua_pop(L, 1);
        actualType = lua_Type.LUA_TNIL;
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
        if (!TryGet(source, key, out L, out lua_Type actualType, out error))
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
        if (!TryGet(source, key, out L, out lua_Type actualType, out error))
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

    /// <summary>
    /// Pushes <paramref name="value"/>. When that throws, pops the <paramref name="pushedCount"/> values the caller
    /// has pushed before, so that a failed access leaves the stack as it found it.
    /// </summary>
    private static void PushOrPop(LuauState state, lua_State* L, in IntoLuau value, int pushedCount)
    {
        try
        {
            value.Push(state);
        }
        catch
        {
            lua_pop(L, pushedCount);
            throw;
        }
    }

    /// <summary> Reads the error object on top of the stack as text and pops it. </summary>
    private static string PopErrorMessage(lua_State* L)
    {
        nuint length = 0;
        byte* message = lua_tolstring(L, -1, &length);
        string error = message is null ? "<unknown lua error>" : Encoding.UTF8.GetString(message, (int)length);
        lua_pop(L, 1);
        return error;
    }
}
