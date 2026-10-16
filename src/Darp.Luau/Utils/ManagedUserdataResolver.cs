using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Native;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Utils;

internal static unsafe class ManagedUserdataResolver
{
    /// <summary> The managed object of the userdata at <paramref name="stackIndex"/>. </summary>
    /// <returns> The object, or null if the value is no managed userdata or its object was released. </returns>
    public static object? GetTarget(LuauState state, lua_State* L, int stackIndex)
    {
        var native = (LuauUserdataNative*)lua_touserdatatagged(L, stackIndex, LuauUserdataNative.Tag);
        return native is null ? null : native->GetTarget(state);
    }

    public static bool TryResolve<T>(
        LuauState state,
        lua_State* L,
        int stackIndex,
        [NotNullWhen(true)] out T? value,
        [NotNullWhen(false)] out string? error,
        string valueLabel
    )
        where T : class
    {
        value = null;
        object? target = GetTarget(state, L, stackIndex);
        if (target is not T typedUserdata)
        {
            error =
                target is not null
                    ? $"{valueLabel} must be userdata of type '{typeof(T).FullName}' but was '{target.GetType().FullName}'."
                : lua_touserdatatagged(L, stackIndex, LuauUserdataNative.Tag) is null
                    ? $"{valueLabel} is not a managed userdata created by this library."
                : $"{valueLabel} userdata was released.";
            return false;
        }

        value = typedUserdata;
        error = null;
        return true;
    }
}
