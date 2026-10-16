using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Native;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Utils;

internal static unsafe class ManagedUserdataResolver
{
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
        var native = (LuauUserdataNative*)lua_touserdatatagged(L, stackIndex, LuauUserdataNative.Tag);
        if (native is null)
        {
            error = $"{valueLabel} is not a managed userdata created by this library.";
            return false;
        }
        object? target = native->GetTarget(state);
        if (target is null)
        {
            error = $"{valueLabel} userdata was released.";
            return false;
        }
        if (target is not T typedUserdata)
        {
            error =
                $"{valueLabel} must be userdata of type '{typeof(T).FullName}' but was '{target.GetType().FullName}'.";
            return false;
        }

        value = typedUserdata;
        error = null;
        return true;
    }
}
