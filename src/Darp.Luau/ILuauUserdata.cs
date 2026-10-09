namespace Darp.Luau;

/// <summary> A managed type that Luau can hold as userdata. </summary>
/// <typeparam name="TSelf">The type itself.</typeparam>
/// <remarks>
/// Prefer <see cref="LuauUserdataAttribute"/>, which generates the implementation. Implement the interface by hand
/// for what the generator cannot express, such as member names that are only known at run time.
/// </remarks>
public interface ILuauUserdata<TSelf>
    where TSelf : class
{
    /// <summary> Describes what Luau can do with an instance: its members and metamethods. </summary>
    /// <param name="registry">Receives the description.</param>
    /// <remarks>
    /// Called once per process, before the first instance reaches a state. The description is shared by every
    /// state, so it must not capture one: a callback gets the state it runs in through its arguments.
    /// </remarks>
    static abstract void Register(LuauUserdataRegistry<TSelf> registry);
}
