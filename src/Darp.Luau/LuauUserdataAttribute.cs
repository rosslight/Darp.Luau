namespace Darp.Luau;

/// <summary> Marks a managed class as a generator-owned Luau userdata surface. </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class LuauUserdataAttribute : Attribute
{
    /// <summary> Initializes a new instance of the <see cref="LuauUserdataAttribute"/> class. </summary>
    /// <param name="name">
    /// The name scripts see: the result of <c>typeof(value)</c>, also used in Luau's errors. It must be a Luau
    /// identifier that is not the name of a built-in Luau type.
    /// </param>
    public LuauUserdataAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary> Gets the name scripts see. </summary>
    public string Name { get; }
}
