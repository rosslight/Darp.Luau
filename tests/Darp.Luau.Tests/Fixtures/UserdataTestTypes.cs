namespace Darp.Luau.Tests.Fixtures;

internal sealed class ValueUserdata : ILuauUserdata<ValueUserdata>
{
    public int Value { get; set; }

    public static void Register(LuauUserdataRegistry<ValueUserdata> registry) { }

    public static implicit operator IntoLuau(ValueUserdata value) => IntoLuau.FromUserdata(value);
}

internal sealed class OtherValueUserdata : ILuauUserdata<OtherValueUserdata>
{
    public static void Register(LuauUserdataRegistry<OtherValueUserdata> registry) { }

    public static implicit operator IntoLuau(OtherValueUserdata value) => IntoLuau.FromUserdata(value);
}
