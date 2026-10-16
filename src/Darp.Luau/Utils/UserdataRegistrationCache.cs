using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Darp.Luau.Native;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Utils;

/// <summary> The userdata types and userdata values of one state. </summary>
internal sealed class UserdataRegistrationCache(LuauState state) : IDisposable
{
    // While a type builds its static side, so that a value of the static side cannot ask for the same table.
    private const int TypeTableIsBuilding = LUA_NOREF - 1;

    [SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "The cache references LuauState but does not own it; LuauState owns and disposes the cache instead."
    )]
    private readonly LuauState _state = state;
    private readonly Dictionary<Type, UserdataType> _types = [];
    private readonly int _identityMapReference = CreateIdentityMapReference(state);
    private bool _userdataDestructorRegistered;
    private int _createdSinceGarbageCollectorStep;

    // A userdata is 24 bytes to Luau. It also holds a managed object and its slot in the state, which Luau does
    // not count. Its collector is paced by what it counts, so it is told the rest.
    private const int UncountedBytesPerUserdata = 256;
    private const int CreationsPerGarbageCollectorStep = 64;

    /// <summary> A userdata type in this state: its description, and what the state built from it. </summary>
    private sealed class UserdataType(LuauState state, UserdataDescription description)
    {
        public LuauState State { get; } = state;
        public UserdataDescription Description { get; } = description;

        /// <summary> The context its members share. Luau owns the handle of this object through it. </summary>
        public int ContextReference { get; set; } = LUA_NOREF;
        public int MetatableReference { get; set; } = LUA_NOREF;

        /// <summary> The static side, which is built when it is first asked for. </summary>
        public int TypeTableReference { get; set; } = LUA_NOREF;
    }

    public unsafe LuauUserdata GetOrCreate<T>(T userdata)
        where T : class, ILuauUserdata<T>
    {
#if DEBUG
        using var guard = new StackGuard(_state.L, expectedDelta: 0);
#endif
        Push(userdata);
        ulong reference = _state.ReferenceTracker.TrackAndPopRef(_state.L, -1);
        return new LuauUserdata(_state, reference);
    }

    /// <summary> Pushes the userdata <paramref name="userdata"/> was last pushed as, or a new one. </summary>
    public unsafe void Push<T>(T userdata)
        where T : class, ILuauUserdata<T>
    {
        lua_State* L = _state.L;

        UserdataObjects objects = _state.UserdataObjects;
        if (objects.TryGetSlot(userdata, out int cachedSlot) && TryPushCachedUserdata(L, cachedSlot, userdata))
            return;

        UserdataType type = GetOrAddType<T>();
        var ptr = (LuauUserdataNative*)darp_luau_newuserdatawithmetatable(
            L,
            (nuint)sizeof(LuauUserdataNative),
            LuauUserdataNative.Tag,
            type.MetatableReference
        );
        // The identity map is keyed by the slot: slots are reused, so its keys stay as few as the live userdata
        (int slot, int generation) = objects.Add(userdata);
        ptr->State = _state.SelfHandle;
        ptr->Slot = slot;
        ptr->Generation = generation;
        nint key = slot;

        // stack: [userdata]
        lua_getref(L, _identityMapReference); // [userdata, identityMap]
        lua_pushlightuserdatatagged(L, (void*)key, 0); // [userdata, identityMap, key]
        lua_pushvalue(L, -3); // [userdata, identityMap, key, userdata]
        lua_rawset(L, -3); // [userdata, identityMap]
        lua_pop(L, 1); // [userdata]

        if (++_createdSinceGarbageCollectorStep == CreationsPerGarbageCollectorStep)
        {
            _createdSinceGarbageCollectorStep = 0;
            _ = lua_gc(
                L,
                (int)lua_GCOp.LUA_GCSTEP,
                CreationsPerGarbageCollectorStep * UncountedBytesPerUserdata / 1024
            );
        }
    }

    /// <summary> Gets the static side of <typeparamref name="T"/>: a read-only table of its functions and values. </summary>
    public unsafe LuauTable GetTypeTable<T>()
        where T : class, ILuauUserdata<T>
    {
        UserdataType type = GetOrAddType<T>();
        if (type.TypeTableReference == TypeTableIsBuilding)
        {
            throw new InvalidOperationException(
                $"A static value of userdata type '{typeof(T)}' cannot use the static side it is part of."
            );
        }
        if (type.TypeTableReference == LUA_NOREF)
            BuildTypeTable(type);

        lua_State* L = _state.L;
        lua_getref(L, type.TypeTableReference);
        ulong reference = _state.ReferenceTracker.TrackAndPopRef(L, -1);
        return new LuauTable(_state, reference);
    }

    public unsafe void Dispose()
    {
        if (!_state.IsDisposed && _identityMapReference != 0)
            _ = lua_unref(_state.L, _identityMapReference);

        // The handles of the types belong to their member contexts, which Luau frees when the state closes.
        _types.Clear();
    }

    private UserdataType GetOrAddType<T>()
        where T : class, ILuauUserdata<T>
    {
        if (_types.TryGetValue(typeof(T), out UserdataType? type))
            return type;

        // Registered before any value of the type is pushed: a static value of the type can be one of its own.
        type = CreateType(UserdataDescription<T>.Value);
        _types.Add(typeof(T), type);
        return type;
    }

    private unsafe UserdataType CreateType(UserdataDescription description)
    {
        lua_State* L = _state.L;
        if (!_userdataDestructorRegistered)
        {
            lua_setuserdatadtor(L, LuauUserdataNative.Tag, &UserdataDestructor);
            _userdataDestructorRegistered = true;
        }

        var type = new UserdataType(_state, description);
        int initialTop = lua_gettop(L);
        try
        {
            lua_newtable(L);
            int metatable = lua_gettop(L);

            // A script must not get or change the metatable: every value of the type shares it.
            fixed (byte* pMetatableName = "__metatable\0"u8)
            {
                LuauStateMarshal.PushString(L, "The metatable is locked"u8);
                lua_rawsetfield(L, metatable, pMetatableName);
            }
            if (description.TypeName is not null)
            {
                fixed (byte* pTypeName = description.TypeName)
                fixed (byte* pTypeKey = "__type\0"u8)
                {
                    lua_pushstring(L, pTypeName);
                    lua_rawsetfield(L, metatable, pTypeKey);
                }
            }

            // From here on Luau owns the handle: it releases it once the last member of the type is gone.
            GCHandle handle = GCHandle.Alloc(type);
            darp_luau_pushmembercontext(L, &MemberCallback, (void*)GCHandle.ToIntPtr(handle), &MemberContextDestructor);
            int context = lua_gettop(L);
            type.ContextReference = lua_ref(L, context);

            SetMemberFunctions(L, metatable, context, description.Metamethods);

            // What a script can read by name: a method is its function, a getter the number of its member.
            lua_newtable(L);
            int readable = lua_gettop(L);
            SetMemberFunctions(L, readable, context, description.Methods);
            SetMemberNumbers(L, readable, description.Getters);
            lua_newtable(L);
            SetMemberNumbers(L, lua_gettop(L), description.Setters);

            // stack: [metatable, context, readable, setters]
            darp_luau_setmemberaccess(L, metatable, description.IndexMember, description.NewIndexMember);
            lua_setreadonly(L, metatable, 1);
            type.MetatableReference = lua_ref(L, metatable);
            return type;
        }
        finally
        {
            lua_settop(L, initialTop);
        }
    }

    private unsafe void BuildTypeTable(UserdataType type)
    {
        lua_State* L = _state.L;
        UserdataDescription description = type.Description;
        int initialTop = lua_gettop(L);
        type.TypeTableReference = TypeTableIsBuilding;
        // A value factory is code of the host. The state must not be closed under the table it is filling.
        _state.VmDepth++;
        try
        {
            lua_newtable(L);
            int table = lua_gettop(L);
            lua_getref(L, type.ContextReference);
            SetMemberFunctions(L, table, lua_gettop(L), description.StaticFunctions);

            foreach (UserdataStaticValue value in description.StaticValues)
            {
                value.Factory(_state).Push(_state, L);
                fixed (byte* pName = value.Name)
                    lua_rawsetfield(L, table, pName);
            }

            // Every script of the state shares the table.
            lua_setreadonly(L, table, 1);
            type.TypeTableReference = lua_ref(L, table);
        }
        catch
        {
            type.TypeTableReference = LUA_NOREF;
            throw;
        }
        finally
        {
            lua_settop(L, initialTop);
            _state.VmDepth--;
        }
    }

    private static unsafe void SetMemberFunctions(lua_State* L, int table, int context, UserdataMemberName[] members)
    {
        foreach (UserdataMemberName member in members)
        {
            fixed (byte* pName = member.Name)
                darp_luau_setmemberfunction(L, table, pName, context, member.Member);
        }
    }

    private static unsafe void SetMemberNumbers(lua_State* L, int table, UserdataMemberName[] members)
    {
        foreach (UserdataMemberName member in members)
        {
            lua_pushinteger(L, member.Member);
            fixed (byte* pName = member.Name)
                lua_rawsetfield(L, table, pName);
        }
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
            lua_rawsetfield(L, -2, pModeName);
        }

        _ = lua_setmetatable(L, -2);
        return LuauNativeMethods.luaL_ref(L, LUA_REGISTRYINDEX);
    }

    // Leaves the userdata on the stack if it is still the one of the object
    private unsafe bool TryPushCachedUserdata(lua_State* L, int slot, object userdata)
    {
        lua_getref(L, _identityMapReference); // [cache]
        lua_pushlightuserdatatagged(L, (void*)(nint)slot, 0); // [cache, key]
        _ = lua_rawget(L, -2); // [cache, value]

        if ((lua_Type)lua_type(L, -1) is not lua_Type.LUA_TUSERDATA)
        {
            lua_pop(L, 2);
            return false;
        }

        // The userdata the object was pushed as can be gone, and its key can belong to that of another object.
        var native = (LuauUserdataNative*)lua_touserdatatagged(L, -1, LuauUserdataNative.Tag);
        if (
            native is null
            || native->Slot != slot
            || !ReferenceEquals(native->Target, userdata)
        )
        {
            lua_pop(L, 2);
            return false;
        }

        lua_remove(L, -2); // [value]
        return true;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void UserdataDestructor(lua_State* _, void* pUserdata)
    {
        if (pUserdata is null)
            return;
        var native = (LuauUserdataNative*)pUserdata;
        if (native->State != 0 && GCHandle.FromIntPtr(native->State).Target is LuauState state)
            state.UserdataObjects.Free(native->Slot, native->Generation);
    }

    /// <summary> Runs when Luau frees the last metatable and function of a userdata type. </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void MemberContextDestructor(void* ctx) => GCHandle.FromIntPtr((IntPtr)ctx).Free();

    /// <summary> Runs a member of a userdata type: Luau has already found out which one a script means. </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int MemberCallback(lua_State* L, void* ctx, int member)
    {
        int top = lua_gettop(L);
        try
        {
            if (GCHandle.FromIntPtr((IntPtr)ctx).Target is not UserdataType type)
                return LuauStateMarshal.ReturnError(L, "userdata type is not registered"u8);
            if (!type.State.OwnsThread(L))
                return LuauStateMarshal.ReturnError(L, "userdata type belongs to a different state"u8);

            return type.Description.Members[member].Invoke(type.State, L);
        }
        catch (Exception exception)
        {
            lua_settop(L, top);
            return LuauStateMarshal.ReturnCallbackException(L, exception);
        }
    }
}
