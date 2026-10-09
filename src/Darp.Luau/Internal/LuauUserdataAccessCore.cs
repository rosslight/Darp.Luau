using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Native;
using Darp.Luau.Utils;

namespace Darp.Luau.Internal;

internal static unsafe class LuauUserdataAccessCore
{
    internal static bool TryGetManaged<T, TUserData>(
        scoped in T source,
        [NotNullWhen(true)] out TUserData? value,
        [NotNullWhen(false)] out string? error
    )
        where T : IReferenceSource, allows ref struct
        where TUserData : class, ILuauUserdata<TUserData>
    {
        value = null;
        LuauState state = source.Validate();
        lua_State* L = state.L;
#if DEBUG
        using var guard = new StackGuard(L, expectedDelta: 0);
#endif
        using PopDisposable _ = source.PushToStack(out int stackIndex);
        return ManagedUserdataResolver.TryResolve(L, stackIndex, out value, out error, typeof(TUserData).Name);
    }
}
