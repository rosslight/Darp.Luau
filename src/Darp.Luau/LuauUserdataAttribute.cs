namespace Darp.Luau;

/// <summary> Marks a managed class as a generator-owned Luau userdata surface. </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class LuauUserdataAttribute : Attribute
{
    /// <summary> Initializes a new instance of the <see cref="LuauUserdataAttribute"/> class for a type without a name. </summary>
    /// <remarks> <c>typeof(value)</c> is <c>userdata</c> for such a type. </remarks>
    public LuauUserdataAttribute() { }

    /// <summary> Initializes a new instance of the <see cref="LuauUserdataAttribute"/> class. </summary>
    /// <param name="name">The name scripts see: the result of <c>typeof(value)</c>, also used in Luau's errors.</param>
    public LuauUserdataAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary> Gets the name scripts see, if the type has one. </summary>
    public string? Name { get; }
}
