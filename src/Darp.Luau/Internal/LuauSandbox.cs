using System.Runtime.InteropServices;
using System.Text;
using Darp.Luau.Native;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Internal;

/// <summary> What a sandboxed state tells the compiler about its globals. </summary>
/// <remarks>
/// A sandboxed state marks its globals as safe. Luau then resolves <c>a.b.c</c> once when a script is loaded and
/// calls the built-in functions it knows without looking them up. Both are only right for what cannot change, so
/// the compiler is told which globals can, and which built-in functions are not the ones it knows.
/// </remarks>
internal sealed unsafe class LuauSandbox : IDisposable
{
    /// <summary> Globals whose members are looked up every time. Null-terminated, or null when there are none. </summary>
    public byte** MutableGlobals { get; private set; }

    /// <summary> Built-in functions that are called like any other function. Null-terminated, or null. </summary>
    public byte** DisabledBuiltins { get; private set; }

    /// <summary>
    /// Whether a script may be loaded with globals that Luau trusts. Not when the globals of the state have a
    /// metatable: it can give a name another value every time, and no list of names covers that.
    /// </summary>
    public bool AllowsFastPaths { get; }

    private LuauSandbox(byte** mutableGlobals, byte** disabledBuiltins, bool allowsFastPaths)
    {
        MutableGlobals = mutableGlobals;
        DisabledBuiltins = disabledBuiltins;
        AllowsFastPaths = allowsFastPaths;
    }

    /// <summary> Looks at the globals of a state that has just been frozen. </summary>
    public static LuauSandbox Create(LuauState state)
    {
        lua_State* L = state.L;
        if (lua_checkstack(L, 8) == 0)
            throw new InvalidOperationException("The Luau stack is full.");
        HashSet<string> mutableGlobals = FindGlobalsThatChange(L);
        List<string> disabledBuiltins = FindBuiltinsThatAreNotTheOriginals(L, mutableGlobals);
        bool hasMetatable = lua_getmetatable(L, LUA_GLOBALSINDEX) != 0;
        if (hasMetatable)
            lua_pop(L, 1);
        return new LuauSandbox(Allocate(mutableGlobals), Allocate(disabledBuiltins), allowsFastPaths: !hasMetatable);
    }

    /// <summary> Sets the options every script of the state is compiled with. </summary>
    public void Apply(ref lua_CompileOptions options)
    {
        options.mutableGlobals = MutableGlobals;
        options.disabledBuiltins = DisabledBuiltins;
    }

    /// <summary>
    /// A global does not change when reading <c>global.a.b</c> gives the same value every time: a frozen table
    /// of values, functions and such tables. A userdata decides what a member is when it is read, and so does a
    /// table with a metatable. A table that is not frozen can be given another member.
    /// </summary>
    private static HashSet<string> FindGlobalsThatChange(lua_State* L)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        lua_pushvalue(L, LUA_GLOBALSINDEX);
        int globals = lua_gettop(L);
        for (int i = lua_rawiter(L, globals, 0); i >= 0; i = lua_rawiter(L, globals, i)) // [globals, key, value]
        {
            if ((lua_Type)lua_type(L, -2) is lua_Type.LUA_TSTRING && Changes(L))
                names.Add(ReadString(L, -2));
            lua_pop(L, 2);
        }
        lua_pop(L, 1);
        return names;
    }

    /// <summary> Whether the members of the value on top can change. The globals are frozen, their tables too. </summary>
    private static bool Changes(lua_State* L)
    {
        var type = (lua_Type)lua_type(L, -1);
        if (type is lua_Type.LUA_TUSERDATA)
            return true;
        if (type is not lua_Type.LUA_TTABLE)
            return false;
        if (HasMetatable(L))
            return true;

        int table = lua_gettop(L);
        for (int i = lua_rawiter(L, table, 0); i >= 0; i = lua_rawiter(L, table, i)) // [table, key, member]
        {
            var member = (lua_Type)lua_type(L, -1);
            bool changes =
                member is lua_Type.LUA_TUSERDATA
                || (member is lua_Type.LUA_TTABLE && (lua_getreadonly(L, -1) == 0 || HasMetatable(L)));
            lua_pop(L, 2);
            if (changes)
                return true;
        }
        return false;
    }

    private static bool HasMetatable(lua_State* L)
    {
        if (lua_getmetatable(L, -1) == 0)
            return false;
        lua_pop(L, 1);
        return true;
    }

    /// <summary>
    /// The compiler knows built-in functions by their name. One that the host replaced, removed or never loaded
    /// must be called by what the name holds now, so every name that does not hold the function of a state with
    /// all libraries is listed.
    /// </summary>
    /// <remarks>
    /// The list alone is not enough. The compiler drops <c>math.floor</c> from it when the script has a global
    /// named <c>floor</c>, and it does not consult it for loops over <c>next</c>, <c>pairs</c> and
    /// <c>ipairs</c>. So the name, or the library it is in, also becomes a global that changes.
    /// </remarks>
    private static List<string> FindBuiltinsThatAreNotTheOriginals(lua_State* L, HashSet<string> mutableGlobals)
    {
        var names = new List<string>();
        using var original = new LuauState(LuauLibraries.All);
        lua_State* O = original.L;
        lua_pushvalue(O, LUA_GLOBALSINDEX);
        int globals = lua_gettop(O);
        for (int i = lua_rawiter(O, globals, 0); i >= 0; i = lua_rawiter(O, globals, i)) // O: [globals, name, value]
        {
            if ((lua_Type)lua_type(O, -2) is lua_Type.LUA_TSTRING)
            {
                PushSameString(O, -2, L);
                _ = lua_rawget(L, LUA_GLOBALSINDEX); // L: [value]
                switch ((lua_Type)lua_type(O, -1))
                {
                    case lua_Type.LUA_TFUNCTION:
                        if (!IsSameFunction(O, L))
                        {
                            names.Add(ReadString(O, -2));
                            mutableGlobals.Add(ReadString(O, -2));
                        }
                        break;
                    case lua_Type.LUA_TTABLE:
                        if (AddLibraryFunctionsThatDiffer(O, L, ReadString(O, -2), names))
                            mutableGlobals.Add(ReadString(O, -2));
                        break;
                }
                lua_pop(L, 1);
            }
            lua_pop(O, 2);
        }
        lua_pop(O, 1);
        return names;
    }

    /// <summary> Compares the functions of the library on top of <paramref name="O"/> with the value on top of <paramref name="L"/>. </summary>
    /// <returns>Whether a function of the library differs.</returns>
    private static bool AddLibraryFunctionsThatDiffer(lua_State* O, lua_State* L, string library, List<string> names)
    {
        int namesBefore = names.Count;
        bool isTable = (lua_Type)lua_type(L, -1) is lua_Type.LUA_TTABLE;
        int table = lua_gettop(O);
        for (int i = lua_rawiter(O, table, 0); i >= 0; i = lua_rawiter(O, table, i)) // O: [library, name, function]
        {
            if (
                (lua_Type)lua_type(O, -2) is lua_Type.LUA_TSTRING
                && (lua_Type)lua_type(O, -1) is lua_Type.LUA_TFUNCTION
            )
            {
                bool isSame = false;
                if (isTable)
                {
                    PushSameString(O, -2, L);
                    _ = lua_rawget(L, -2); // L: [library, function]
                    isSame = IsSameFunction(O, L);
                    lua_pop(L, 1);
                }
                if (!isSame)
                    names.Add($"{library}.{ReadString(O, -2)}");
            }
            lua_pop(O, 2);
        }
        return names.Count > namesBefore;
    }

    /// <summary> A built-in function is a native function, the same one in every state of the process. </summary>
    private static bool IsSameFunction(lua_State* O, lua_State* L) =>
        lua_iscfunction(L, -1) != 0 && (nint)lua_tocfunction(L, -1) == (nint)lua_tocfunction(O, -1);

    private static void PushSameString(lua_State* from, int index, lua_State* to)
    {
        nuint length = 0;
        byte* text = lua_tolstring(from, index, &length);
        lua_pushlstring(to, text, length);
    }

    private static string ReadString(lua_State* L, int index)
    {
        nuint length = 0;
        byte* text = lua_tolstring(L, index, &length);
        return Encoding.UTF8.GetString(text, checked((int)length));
    }

    /// <summary> One block: the pointers, a null pointer, then the names as zero-terminated UTF-8. </summary>
    private static byte** Allocate(IReadOnlyCollection<string> source)
    {
        string[] names = [.. source];
        if (names.Length == 0)
            return null;

        int textLength = 0;
        foreach (string name in names)
            textLength += Encoding.UTF8.GetByteCount(name) + 1;
        nuint pointersLength = (nuint)(names.Length + 1) * (nuint)sizeof(byte*);
        var array = (byte**)NativeMemory.Alloc(pointersLength + (nuint)textLength);
        byte* text = (byte*)array + pointersLength;
        for (int i = 0; i < names.Length; i++)
        {
            array[i] = text;
            int written = Encoding.UTF8.GetBytes(names[i], new Span<byte>(text, textLength));
            text[written] = 0;
            text += written + 1;
            textLength -= written + 1;
        }
        array[names.Length] = null;
        return array;
    }

    public void Dispose()
    {
        NativeMemory.Free(MutableGlobals);
        NativeMemory.Free(DisabledBuiltins);
        MutableGlobals = null;
        DisabledBuiltins = null;
    }
}
