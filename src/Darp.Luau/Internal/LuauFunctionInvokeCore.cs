using System.Runtime.CompilerServices;
using Darp.Luau.Native;
using Darp.Luau.Utils;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Internal;

internal static unsafe class LuauFunctionInvokeCore
{
    private const int LuaMultRet = -1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Invoke<T>(scoped in T? source, scoped in RefEnumerable<IntoLuau> args)
        where T : IReferenceSource, allows ref struct
    {
        LuauState state = source.Validate();
        lua_State* L = state.L;
#if DEBUG
        using var guard = new StackGuard(L, expectedDelta: 0);
#endif
        int topBeforeInvoke = lua_gettop(L);
        try
        {
            source.PushToTop();
            int length = args.Length;
            for (int i = 0; i < length; i++)
                args[i].Push(state);

            int status = lua_pcall(L, length, 0, 0);
            LuaException.ThrowIfNotOk(L, status, "lua_pcall");
        }
        finally
        {
            lua_settop(L, topBeforeInvoke);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TR Invoke<T, TR>(scoped in T? source, scoped in RefEnumerable<IntoLuau> args, Func<LuauArgs, TR> func)
        where T : IReferenceSource, allows ref struct
    {
        LuauState state = source.Validate();
        lua_State* L = state.L;
#if DEBUG
        using var guard = new StackGuard(L, expectedDelta: 0);
#endif
        int topBeforeInvoke = lua_gettop(L);
        try
        {
            source.PushToTop();
            int nArgs = args.Length;
            for (int i = 0; i < nArgs; i++)
                args[i].Push(state);

            int status = lua_pcall(L, nArgs, LuaMultRet, 0);
            LuaException.ThrowIfNotOk(L, status, "lua_pcall");
            using LuauCallFrame frame = state.BeginLuauCallFrame();
            var result = new LuauArgs(state.L, lua_gettop(L) - topBeforeInvoke, topBeforeInvoke + 1, frame);
            return func(result);
        }
        finally
        {
            lua_settop(L, topBeforeInvoke);
        }
    }

    public static TR ResultSelector<TR>(LuauArgs a) => a.Read<TR>(1);

    public static (TR1, TR2) ResultSelector<TR1, TR2>(LuauArgs a) => (a.Read<TR1>(1), a.Read<TR2>(2));

    public static (TR1, TR2, TR3) ResultSelector<TR1, TR2, TR3>(LuauArgs a) =>
        (a.Read<TR1>(1), a.Read<TR2>(2), a.Read<TR3>(3));

    public static (TR1, TR2, TR3, TR4) ResultSelector<TR1, TR2, TR3, TR4>(LuauArgs a) =>
        (a.Read<TR1>(1), a.Read<TR2>(2), a.Read<TR3>(3), a.Read<TR4>(4));

    public static LuauValue[] ResultSelectorMulti(LuauArgs a)
    {
        var values = new LuauValue[a.ArgumentCount];
        for (int i = 1; i <= values.Length; i++)
        {
            if (!a.TryReadLuauValue(i, out LuauValue value, out string? error))
                throw new ArgumentOutOfRangeException(nameof(a), error);
            values[i - 1] = value;
        }

        return values;
    }
}
