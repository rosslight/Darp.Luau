using System.Diagnostics;
using System.Text;
using Darp.Luau.Native;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Utils;

/// <summary> Something of a userdata type that Luau calls: an accessor, a method, a metamethod or a static function. </summary>
internal abstract class UserdataMember(string label)
{
    /// <summary> Names the member in the message of an exception it throws. </summary>
    public string Label { get; } = label;

    /// <summary> Runs the member with the arguments on the stack of <paramref name="L"/>. </summary>
    /// <returns>The result of the native callback: a value count, an error, or a yield.</returns>
    public abstract unsafe int Invoke(LuauState state, lua_State* L);
}

internal sealed class UserdataGetter<T>(string name, LuauGetter<T> getter) : UserdataMember($"userdata member '{name}'")
    where T : class
{
    private readonly LuauGetter<T> _getter = getter;

    // stack: [self, key]
    public override unsafe int Invoke(LuauState state, lua_State* L)
    {
        if (!ManagedUserdataResolver.TryResolve(L, 1, out T? self, out string? error, valueLabel: "self"))
            return LuauStateMarshal.ReturnError(L, error);

        return _getter(self, state).TryPushValue(state, L, out error)
            ? LuauStateMarshal.ReturnSuccess(L, 1)
            : LuauStateMarshal.ReturnError(L, error);
    }
}

internal sealed class UserdataSetter<T>(string name, LuauSetter<T> setter) : UserdataMember($"userdata member '{name}'")
    where T : class
{
    private readonly LuauSetter<T> _setter = setter;

    // stack: [self, key, value]
    public override unsafe int Invoke(LuauState state, lua_State* L)
    {
        if (!ManagedUserdataResolver.TryResolve(L, 1, out T? self, out string? error, valueLabel: "self"))
            return LuauStateMarshal.ReturnError(L, error);

        var value = new LuauArgsSingle(new LuauArgs(state, L, argumentCount: 1, firstParameterStackIndex: 3));
        return _setter(self, value).TryGetError(out error)
            ? LuauStateMarshal.ReturnError(L, error)
            : LuauStateMarshal.ReturnSuccess(L, 0);
    }
}

internal sealed class UserdataMethod<T>(string name, LuauMethod<T> method) : UserdataMember($"userdata method '{name}'")
    where T : class
{
    private readonly string _name = name;
    private readonly LuauMethod<T> _method = method;

    // stack: [self, arguments...]
    public override unsafe int Invoke(LuauState state, lua_State* L)
    {
        // A method is a value, so a script can call it with anything: value.name(1) instead of value:name(1).
        if (!ManagedUserdataResolver.TryResolve(L, 1, out T? self, out _, valueLabel: "self"))
        {
            return LuauStateMarshal.ReturnError(
                L,
                $"userdata method '{_name}' must be called on its instance, as in value:{_name}(...)"
            );
        }

        int top = lua_gettop(L);
        var args = new LuauArgs(state, L, argumentCount: top - 1, firstParameterStackIndex: 2);
        LuauReturn result = _method(self, args);
        lua_settop(L, top);
        return LuauStateMarshal.ReturnCallbackResult(state, L, result);
    }
}

/// <summary> A member that gets every argument as Luau passed it: a metamethod or a static function. </summary>
internal sealed class UserdataCallback(string label, LuauCallback callback) : UserdataMember(label)
{
    private readonly LuauCallback _callback = callback;

    public override unsafe int Invoke(LuauState state, lua_State* L)
    {
        int top = lua_gettop(L);
        var args = new LuauArgs(state, L, argumentCount: top, firstParameterStackIndex: 1);
        LuauReturn result = _callback(args);
        Debug.Assert(lua_gettop(L) == top);
        return LuauStateMarshal.ReturnCallbackResult(state, L, result);
    }
}

/// <summary> A name as Luau reads it, and the number of the member it stands for. </summary>
/// <param name="Name">The name in UTF-8, with a terminating zero.</param>
/// <param name="Member">Index into <see cref="UserdataDescription.Members"/>.</param>
internal readonly record struct UserdataMemberName(byte[] Name, int Member);

/// <param name="Name">The name in UTF-8, with a terminating zero.</param>
/// <param name="DisplayName">The name as it was declared, for error messages.</param>
/// <param name="Factory">Creates the value for one state.</param>
internal readonly record struct UserdataStaticValue(byte[] Name, string DisplayName, LuauValueFactory Factory);

/// <summary> What Luau can do with a managed type. It holds no state, so every state of the process shares it. </summary>
internal sealed class UserdataDescription
{
    /// <summary> The member number that tells Luau there is no fallback for unknown keys. </summary>
    public const int NoMember = -1;

    /// <summary> The name scripts see, in UTF-8 with a terminating zero; null when the type has none. </summary>
    public required byte[]? TypeName { get; init; }
    public required UserdataMember[] Members { get; init; }
    public required UserdataMemberName[] Methods { get; init; }
    public required UserdataMemberName[] Getters { get; init; }
    public required UserdataMemberName[] Setters { get; init; }

    /// <summary> Every metamethod but <c>__index</c> and <c>__newindex</c>, which resolve the members first. </summary>
    public required UserdataMemberName[] Metamethods { get; init; }
    public required int IndexMember { get; init; }
    public required int NewIndexMember { get; init; }
    public required UserdataMemberName[] StaticFunctions { get; init; }
    public required UserdataStaticValue[] StaticValues { get; init; }

    public static byte[] ToLuauName(string name)
    {
        byte[] utf8 = new byte[Encoding.UTF8.GetByteCount(name) + 1];
        Encoding.UTF8.GetBytes(name, utf8);
        return utf8;
    }
}

/// <summary> The description of <typeparamref name="T"/>, built by its <c>Register</c> the first time it is needed. </summary>
internal static class UserdataDescription<T>
    where T : class, ILuauUserdata<T>
{
    // A failed registration is kept as well: every later use reports the same error.
    private static readonly Lazy<UserdataDescription> s_value = new(Create);

    public static UserdataDescription Value => s_value.Value;

    private static UserdataDescription Create()
    {
        var registry = new LuauUserdataRegistry<T>();
        T.Register(registry);
        return registry.Build();
    }
}
