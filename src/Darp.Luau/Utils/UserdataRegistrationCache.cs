using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Darp.Luau.Native;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Utils;

internal sealed class UserdataRegistrationCache(LuauState state) : IDisposable
{
    [SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "The cache references LuauState but does not own it; LuauState owns and disposes the cache instead."
    )]
    private readonly LuauState _state = state;
    private readonly Dictionary<Type, GCHandle> _registrations = [];
    private readonly ConditionalWeakTable<object, ObjectIdentity> _identityByUserdata = new();
    private readonly int _identityMapReference = CreateIdentityMapReference(state);
    private int _nextIdentity;
    private bool _userdataCallbacksRegistered;

    private sealed class ObjectIdentity(int value)
    {
        public int Value { get; } = value;
    }

    public unsafe GCHandle Register<T>()
        where T : class, ILuauUserData<T>
    {
        if (_registrations.TryGetValue(typeof(T), out GCHandle handle))
            return handle;

        EnsureUserdataCallbacksRegistered();

        var callbackRegistration = new UserdataCallbackRegistration(
            _state,
            IndexCallbackManaged,
            NewIndexCallbackManaged,
            MethodCallbackManaged
        );
        handle = GCHandle.Alloc(callbackRegistration);
        _registrations.Add(typeof(T), handle);
        return handle;

        static LuaResult<int, string> IndexCallbackManaged(LuauState state, lua_State* L, object? userdata)
        {
            if (userdata is not T target)
                return $"Expected userdata of type '{typeof(T).FullName}'.";

            if (!LuauStateMarshal.TryGetString(L, 2, out ReadOnlySpan<byte> utf8MemberName))
                return "userdata index access requires a string member name";

            Span<char> memberName = stackalloc char[Encoding.UTF8.GetCharCount(utf8MemberName)];
            int memberNameLength = Encoding.UTF8.GetChars(utf8MemberName, memberName);
            ReadOnlySpan<char> resolvedMemberName = memberName[..memberNameLength];

            LuauReturnSingle result = T.OnIndex(target, state, resolvedMemberName);

            if (result.TryPushValue(state, L, out string? error))
                return 1;
            if (error == LuauReturn.NotHandled)
                return 0;
            return error;
        }

        static LuaResult<int, string> NewIndexCallbackManaged(LuauState lua, lua_State* L, object? userdata)
        {
            if (userdata is not T target)
                return $"Expected userdata of type '{typeof(T).FullName}'.";

            if (!LuauStateMarshal.TryGetString(L, 2, out ReadOnlySpan<byte> utf8MemberName))
                return "userdata assignment requires a string member name";

            Span<char> memberName = stackalloc char[Encoding.UTF8.GetCharCount(utf8MemberName)];
            int memberNameLength = Encoding.UTF8.GetChars(utf8MemberName, memberName);
            ReadOnlySpan<char> resolvedMemberName = memberName[..memberNameLength];

            var args = new LuauArgs(lua, L, argumentCount: 1, firstParameterStackIndex: 3);
            Debug.Assert(args.ArgumentCount == 1);
            var argsSingle = new LuauArgsSingle(args);
            LuauOutcome result = T.OnSetIndex(target, argsSingle, resolvedMemberName);
            if (!result.TryGetError(out string? error))
            {
                // Success
                return 0;
            }
            if (error == LuauReturn.NotHandled)
                return (string)$"attempt to set unknown userdata member '{resolvedMemberName}'";
            return error;
        }

        static LuaResult<int, string> MethodCallbackManaged(LuauState lua, lua_State* L, object? userdata)
        {
            if (userdata is not T target)
                return $"Expected userdata of type '{typeof(T).FullName}'.";

            int firstParameterStackIndex;
            int numberOfParameters;
            if (LuauStateMarshal.TryGetNameCall(L, out ReadOnlySpan<byte> utf8MethodName))
            {
                numberOfParameters = Math.Max(0, lua_gettop(L) - 1);
                firstParameterStackIndex = 2;
            }
            else if (LuauStateMarshal.TryGetString(L, 2, out utf8MethodName))
            {
                numberOfParameters = Math.Max(0, lua_gettop(L) - 2);
                firstParameterStackIndex = 3;
            }
            else
            {
                return "userdata method call requires a string method name";
            }

            int topBeforeInvoke = lua_gettop(L);
            var functionArgs = new LuauArgs(lua, L, numberOfParameters, firstParameterStackIndex);
            Span<char> methodName = stackalloc char[Encoding.UTF8.GetCharCount(utf8MethodName)];
            int memberNameLength = Encoding.UTF8.GetChars(utf8MethodName, methodName);
            ReadOnlySpan<char> resolvedMethodName = methodName[..memberNameLength];
            try
            {
                LuauReturn result = T.OnMethodCall(target, functionArgs, resolvedMethodName);
                lua_settop(L, topBeforeInvoke);

                if (result.TryPushValues(lua, L, out int outputCount, out string? error))
                    return outputCount;
                if (error == LuauReturn.NotHandled)
                    return (string)$"attempt to call unknown userdata method '{resolvedMethodName}'";
                return error;
            }
            catch
            {
                lua_settop(L, topBeforeInvoke);
                throw;
            }
        }
    }

    public unsafe LuauUserdata GetOrCreate<T>(T userdata)
        where T : class, ILuauUserData<T>
    {
        EnsureUserdataCallbacksRegistered();

        int identity = _identityByUserdata.GetValue(userdata, _ => new ObjectIdentity(++_nextIdentity)).Value;
        lua_State* L = _state.L;
#if DEBUG
        using var guard = new StackGuard(L, expectedDelta: 0);
#endif

        if (TryGetCachedUserdataHandle(L, identity, out ulong cachedHandle))
        {
            return new LuauUserdata(_state, cachedHandle);
        }

        GCHandle registrationHandle = Register<T>();
        var ptr = (LuauUserdataNative*)lua_newuserdatataggedwithmetatable(
            L,
            (nuint)sizeof(LuauUserdataNative),
            LuauUserdataNative.Tag
        );
        ptr->UserdataHandle = GCHandle.Alloc(userdata, GCHandleType.Normal);
        ptr->RegistryValueHandle = registrationHandle;

        // stack: [userdata]
        lua_getref(L, _identityMapReference); // [userdata, identityMap]
        lua_pushinteger(L, identity); // [userdata, identityMap, identity]
        lua_pushvalue(L, -3); // [userdata, identityMap, identity, userdata]
        lua_settable(L, -3); // [userdata, identityMap]
        lua_pop(L, 1); // [userdata]

        ulong reference = _state.ReferenceTracker.TrackAndPopRef(L, -1);
        return new LuauUserdata(_state, reference);
    }

    public unsafe void Dispose()
    {
        if (!_state.IsDisposed && _identityMapReference != 0)
            _ = lua_unref(_state.L, _identityMapReference);

        foreach (GCHandle handle in _registrations.Values)
        {
            if (handle.IsAllocated)
                handle.Free();
        }
        _registrations.Clear();
    }

    private static unsafe int CreateIdentityMapReference(LuauState state)
    {
        lua_State* L = state.L;
#if DEBUG
        using var guard = new StackGuard(L, expectedDelta: 0);
#endif
        lua_newtable(L); // cache
        lua_newtable(L); // metatable

        fixed (byte* pModeName = "__mode\0"u8)
        fixed (byte* pWeakValues = "v"u8)
        {
            lua_pushlstring(L, pWeakValues, 1);
            lua_setfield(L, -2, pModeName);
        }

        _ = lua_setmetatable(L, -2);
        return LuauNativeMethods.luaL_ref(L, LUA_REGISTRYINDEX);
    }

    private unsafe bool TryGetCachedUserdataHandle(lua_State* L, int identity, out ulong handle)
    {
#if DEBUG
        using var guard = new StackGuard(L, expectedDelta: 0);
#endif
        lua_getref(L, _identityMapReference); // [cache]
        lua_pushinteger(L, identity); // [cache, identity]
        _ = lua_gettable(L, -2); // [cache, value]

        if ((lua_Type)lua_type(L, -1) is not lua_Type.LUA_TUSERDATA)
        {
            handle = 0;
            lua_pop(L, 2);
            return false;
        }

        var native = (LuauUserdataNative*)lua_touserdatatagged(L, -1, LuauUserdataNative.Tag);
        if (native is null || !native->UserdataHandle.IsAllocated)
        {
            handle = 0;
            lua_pop(L, 2);
            return false;
        }

        handle = _state.ReferenceTracker.TrackRef(L, -1);
        lua_pop(L, 2);
        return true;
    }

    private unsafe void EnsureUserdataCallbacksRegistered()
    {
        if (_userdataCallbacksRegistered)
            return;

        lua_State* L = _state.L;
        int initialTop = lua_gettop(L);
        try
        {
            lua_newtable(L);

            fixed (byte* pIndexName = "__index\0"u8)
            {
                _state.PushNativeCallback(&IndexCallback, null, pIndexName);
                lua_setfield(L, -2, pIndexName);
            }
            fixed (byte* pNewIndexName = "__newindex\0"u8)
            {
                _state.PushNativeCallback(&NewIndexCallback, null, pNewIndexName);
                lua_setfield(L, -2, pNewIndexName);
            }
            fixed (byte* pNameCallName = "__namecall\0"u8)
            {
                _state.PushNativeCallback(&MethodCallback, null, pNameCallName);
                lua_setfield(L, -2, pNameCallName);
            }

            lua_setuserdatametatable(L, LuauUserdataNative.Tag);
            lua_setuserdatadtor(L, LuauUserdataNative.Tag, &UserdataDestructor);
            _userdataCallbacksRegistered = true;
        }
        finally
        {
            lua_settop(L, initialTop);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void UserdataDestructor(lua_State* _, void* pUserdata)
    {
        if (pUserdata is null)
            return;
        var native = (LuauUserdataNative*)pUserdata;
        if (native->UserdataHandle.IsAllocated)
            native->UserdataHandle.Free();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int IndexCallback(lua_State* L, void* ctx)
    {
        _ = ctx;
        try
        {
            if (!TryGetCallbackRegistration(L, out var registration, out object? userdata, out var errorMessage))
                return LuauStateMarshal.ReturnError(L, errorMessage);
            LuaResult<int, string> result = registration.OnIndexCallback(registration.State, L, userdata);
            return LuauStateMarshal.ReturnResult(L, result);
        }
        catch (Exception exception)
        {
            return LuauStateMarshal.ReturnCallbackException(L, "__index", exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int NewIndexCallback(lua_State* L, void* ctx)
    {
        _ = ctx;
        try
        {
            if (!TryGetCallbackRegistration(L, out var registration, out object? userdata, out var errorMessage))
                return LuauStateMarshal.ReturnError(L, errorMessage);
            LuaResult<int, string> result = registration.OnNewIndexCallback(registration.State, L, userdata);
            return LuauStateMarshal.ReturnResult(L, result);
        }
        catch (Exception exception)
        {
            return LuauStateMarshal.ReturnCallbackException(L, "__newindex", exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int MethodCallback(lua_State* L, void* ctx)
    {
        _ = ctx;
        try
        {
            if (!TryGetCallbackRegistration(L, out var registration, out object? userdata, out var errorMessage))
                return LuauStateMarshal.ReturnError(L, errorMessage);
            LuaResult<int, string> result = registration.OnMethodCallback(registration.State, L, userdata);
            return LuauStateMarshal.ReturnResult(L, result);
        }
        catch (Exception exception)
        {
            return LuauStateMarshal.ReturnCallbackException(L, "__namecall", exception);
        }
    }

    private static unsafe bool TryGetCallbackRegistration(
        lua_State* L,
        [NotNullWhen(true)] out UserdataCallbackRegistration? callbackRegistration,
        out object? userdata,
        out ReadOnlySpan<byte> errorMessage
    )
    {
        callbackRegistration = null!;
        userdata = null;
        errorMessage = default;

        var native = (LuauUserdataNative*)lua_touserdatatagged(L, 1, LuauUserdataNative.Tag);
        if (native is null)
        {
            errorMessage = "invalid userdata value"u8;
            return false;
        }
        if (!native->RegistryValueHandle.IsAllocated)
        {
            errorMessage = "userdata callback registration is not allocated"u8;
            return false;
        }
        if (native->RegistryValueHandle.Target is not UserdataCallbackRegistration resolvedRegistration)
        {
            errorMessage = "userdata callback registration is invalid"u8;
            return false;
        }
        if (!resolvedRegistration.State.OwnsThread(L))
        {
            errorMessage = "userdata callback registration belongs to a different state"u8;
            return false;
        }
        if (!native->UserdataHandle.IsAllocated)
        {
            errorMessage = "userdata handle is not allocated"u8;
            return false;
        }

        userdata = native->UserdataHandle.Target;
        callbackRegistration = resolvedRegistration;
        return true;
    }

    private sealed record UserdataCallbackRegistration(
        LuauState State,
        UserdataCallbackRegistration.OnLuaCallback OnIndexCallback,
        UserdataCallbackRegistration.OnLuaCallback OnNewIndexCallback,
        UserdataCallbackRegistration.OnLuaCallback OnMethodCallback
    )
    {
        public unsafe delegate LuaResult<int, string> OnLuaCallback(LuauState lua, lua_State* L, object? userdata);
    }
}

internal readonly ref struct LuaResult<T, TError>
    where T : allows ref struct
    where TError : allows ref struct
{
    private readonly T? _value;
    private readonly TError? _error;

    public bool IsOk { get; }

    private LuaResult(bool isOk, T? value, TError? error)
    {
        IsOk = isOk;
        _value = value;
        _error = error;
    }

    public bool TryGetValue([NotNullWhen(true)] out T? value, [NotNullWhen(false)] out TError? error)
    {
        if (IsOk)
        {
            value = _value!;
            error = _error;
            return true;
        }
        value = _value;
        error = _error!;
        return false;
    }

    public static implicit operator LuaResult<T, TError>(T value) => new(true, value, default);

    public static implicit operator LuaResult<T, TError>(TError error) => new(false, default, error);
}
