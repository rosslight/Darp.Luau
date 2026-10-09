using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Native;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Utils;

internal static unsafe class ManagedUserdataResolver
{
    public static bool TryGetNative(
        lua_State* L,
        int stackIndex,
        [NotNullWhen(true)] out LuauUserdataNative* native,
        [NotNullWhen(false)] out string? error,
        string valueLabel
    )
    {
        native = (LuauUserdataNative*)lua_touserdatatagged(L, stackIndex, LuauUserdataNative.Tag);
        if (native is null)
        {
            error = $"{valueLabel} is not a managed userdata created by this library.";
            return false;
        }
        if (!native->UserdataHandle.IsAllocated)
        {
            error = $"{valueLabel} userdata handle is not allocated.";
            return false;
        }

        error = null;
        return true;
    }

    public static bool TryResolve<T>(
        lua_State* L,
        int stackIndex,
        [NotNullWhen(true)] out T? value,
        [NotNullWhen(false)] out string? error,
        string valueLabel
    )
        where T : class
    {
        value = null;
        if (!TryGetNative(L, stackIndex, out LuauUserdataNative* native, out error, valueLabel))
            return false;

        object? target = native->UserdataHandle.Target;
        if (target is not T typedUserdata)
        {
            error =
                $"{valueLabel} must be userdata of type '{typeof(T).FullName}' but was '{target?.GetType().FullName ?? "<null>"}'.";
            return false;
        }

        value = typedUserdata;
        error = null;
        return true;
    }
}
