namespace Darp.Luau;

/// <summary> Marks a method or operator of a generator-owned userdata type as one of its metamethods. </summary>
/// <remarks>
/// <para>
/// The method can have any name and accessibility. An instance method takes the instance as the first operand, so
/// for a binary operator the instance is the left one; a static method names every operand, which lets the instance
/// be the right one: <c>2 * vec</c>.
/// </para>
/// <para>
/// Several methods can declare the same metamethod when Luau can tell their operands apart by type. Only
/// <see cref="LuauMetamethod.Call"/> can return a task.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LuauMetamethodAttribute : Attribute
{
    /// <summary> Initializes a new instance of the <see cref="LuauMetamethodAttribute"/> class. </summary>
    /// <param name="metamethod">The metamethod the method implements.</param>
    public LuauMetamethodAttribute(LuauMetamethod metamethod) => Metamethod = metamethod;

    /// <summary> Gets the metamethod the method implements. </summary>
    public LuauMetamethod Metamethod { get; }
}
