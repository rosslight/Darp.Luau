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
    /// <summary>
    /// The built-in functions of a state with all libraries, and the libraries with theirs. A built-in function
    /// is a native function, the same one in every state of the process, so one state is read once.
    /// </summary>
    private static readonly Lazy<OriginalGlobal[]> Originals = new(ReadOriginals);

    /// <summary> Globals whose members are looked up every time. Null-terminated. </summary>
    public byte** MutableGlobals { get; private set; }

    /// <summary> Built-in functions that are called like any other function. Null-terminated. </summary>
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

    /// <summary> Freezes the table on top, every table in it, and so on through every depth. </summary>
    /// <remarks>
    /// A metatable is not entered, nor a userdata or a function: what it holds belongs to whatever made it.
    /// </remarks>
    public static void FreezeTables(lua_State* L)
    {
        if (lua_checkstack(L, 4) == 0)
            throw new InvalidOperationException("The Luau stack is full.");
        // A table can hold itself or a table that holds it.
        var frozen = new HashSet<nint>();
        // The tables still to be walked wait in a table, so a deep nesting takes no stack.
        lua_newtable(L); // [root, pending]
        int pending = lua_gettop(L);
        int pendingCount = 0;
        lua_pushvalue(L, -2); // [root, pending, table]
        while (true)
        {
            if (frozen.Add((nint)lua_topointer(L, -1)))
            {
                lua_setreadonly(L, -1, 1);
                int table = lua_gettop(L);
                for (int i = lua_rawiter(L, table, 0); i >= 0; i = lua_rawiter(L, table, i)) // [.., table, key, value]
                {
                    if (
                        (lua_Type)lua_type(L, -1) is lua_Type.LUA_TTABLE
                        && !frozen.Contains((nint)lua_topointer(L, -1))
                    )
                        lua_rawseti(L, pending, ++pendingCount);
                    else
                        lua_pop(L, 1);
                    lua_pop(L, 1);
                }
            }
            lua_pop(L, 1); // [root, pending]
            if (pendingCount == 0)
                break;
            _ = lua_rawgeti(L, pending, pendingCount--); // [root, pending, table]
        }
        lua_pop(L, 1); // [root]
    }

    /// <summary>
    /// A global does not change when reading <c>global.a.b</c> gives the same value every time: a table of
    /// values, functions and such tables. Every table in the globals is frozen, so only a userdata, which decides
    /// what a member is when it is read, and a table with a metatable, which does the same, can change.
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

    /// <summary> Whether the members of the value on top can change. </summary>
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
            bool changes = member is lua_Type.LUA_TUSERDATA || (member is lua_Type.LUA_TTABLE && HasMetatable(L));
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
        foreach (OriginalGlobal global in Originals.Value)
        {
            LuauStateMarshal.PushString(L, global.Name);
            _ = lua_rawget(L, LUA_GLOBALSINDEX); // [value]
            if (global.Members is { } functions)
            {
                if (AddLibraryFunctionsThatDiffer(L, global.Name, functions, names))
                    mutableGlobals.Add(global.Name);
            }
            else if (!IsFunction(L, global.Function))
            {
                names.Add(global.Name);
                mutableGlobals.Add(global.Name);
            }
            lua_pop(L, 1);
        }
        return names;
    }

    /// <summary> Compares the functions of a library with the value on top. </summary>
    /// <returns>Whether a function of the library differs.</returns>
    private static bool AddLibraryFunctionsThatDiffer(
        lua_State* L,
        string library,
        OriginalGlobal[] functions,
        List<string> names
    )
    {
        int namesBefore = names.Count;
        bool isTable = (lua_Type)lua_type(L, -1) is lua_Type.LUA_TTABLE;
        foreach (OriginalGlobal function in functions)
        {
            bool isSame = false;
            if (isTable)
            {
                LuauStateMarshal.PushString(L, function.Name);
                _ = lua_rawget(L, -2); // [library, function]
                isSame = IsFunction(L, function.Function);
                lua_pop(L, 1);
            }
            if (!isSame)
                names.Add($"{library}.{function.Name}");
        }
        return names.Count > namesBefore;
    }

    private static OriginalGlobal[] ReadOriginals()
    {
        using var original = new LuauState(LuauLibraries.All);
        lua_State* O = original.L;
        lua_pushvalue(O, LUA_GLOBALSINDEX);
        OriginalGlobal[] globals = ReadFunctionsAndLibraries(O, readLibraries: true);
        lua_pop(O, 1);
        return globals;
    }

    /// <summary> Reads the functions of the table on top, and with <paramref name="readLibraries"/> its tables. </summary>
    private static OriginalGlobal[] ReadFunctionsAndLibraries(lua_State* O, bool readLibraries)
    {
        var globals = new List<OriginalGlobal>();
        int table = lua_gettop(O);
        for (int i = lua_rawiter(O, table, 0); i >= 0; i = lua_rawiter(O, table, i)) // [table, name, value]
        {
            if ((lua_Type)lua_type(O, -2) is lua_Type.LUA_TSTRING)
            {
                switch ((lua_Type)lua_type(O, -1))
                {
                    case lua_Type.LUA_TFUNCTION:
                        globals.Add(new OriginalGlobal(ReadString(O, -2), (nint)lua_tocfunction(O, -1), Members: null));
                        break;
                    case lua_Type.LUA_TTABLE when readLibraries:
                        globals.Add(new OriginalGlobal(ReadString(O, -2), 0, ReadFunctionsAndLibraries(O, false)));
                        break;
                }
            }
            lua_pop(O, 2);
        }
        return [.. globals];
    }

    /// <summary> A built-in function, or with <see cref="Members"/> a library of them. </summary>
    private sealed record OriginalGlobal(string Name, nint Function, OriginalGlobal[]? Members);

    private static bool IsFunction(lua_State* L, nint function) =>
        lua_iscfunction(L, -1) != 0 && (nint)lua_tocfunction(L, -1) == function;

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
