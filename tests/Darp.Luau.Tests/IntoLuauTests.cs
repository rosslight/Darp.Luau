using Darp.Luau.Utils;
using Shouldly;

namespace Darp.Luau.Tests;

public sealed class IntoLuauTests
{
    [Theory]
    [InlineData(null, IntoLuau.Kind.Nil)]
    [InlineData(false, IntoLuau.Kind.Bool)]
    internal void Into_Bool(bool? value, IntoLuau.Kind expectedKind)
    {
        IntoLuau intoValue = value;

        intoValue.Type.ShouldBe(expectedKind);
    }

    [Theory]
    [InlineData(null, IntoLuau.Kind.Nil)]
    [InlineData("test", IntoLuau.Kind.Chars)]
    internal void Into_String(string? value, IntoLuau.Kind expectedKind)
    {
        IntoLuau intoValue = value;

        intoValue.Type.ShouldBe(expectedKind);
    }

    [Fact]
    internal void Into_Value()
    {
        IntoLuau intoValue = default(LuauValue);

        intoValue.Type.ShouldBe(IntoLuau.Kind.Value);
    }

    [Fact]
    internal void Into_Table()
    {
        IntoLuau intoValue = default(LuauTable);

        intoValue.Type.ShouldBe(IntoLuau.Kind.Nil);
    }

    [Fact]
    internal void Into_Function()
    {
        IntoLuau intoValue = default(LuauFunction);

        intoValue.Type.ShouldBe(IntoLuau.Kind.Nil);
    }

    [Fact]
    internal void Into_Numbers()
    {
        // ReSharper disable once RedundantCast
        IntoLuau intoValue1 = (sbyte)1;
        IntoLuau intoValue2 = (byte)1;
        // ReSharper disable once RedundantCast
        IntoLuau intoValue3 = (short)1;
        IntoLuau intoValue4 = (ushort)1;
        // ReSharper disable once RedundantCast
        IntoLuau intoValue5 = (int)1;
        IntoLuau intoValue6 = (uint)1;
        IntoLuau intoValue7 = (long)1;
        IntoLuau intoValue8 = (ulong)1;
        IntoLuau intoValue9 = (Half)1;
        IntoLuau intoValue10 = (float)1;
        IntoLuau intoValue11 = (double)1;
        // IntoLuau intoValue12 = (Int128)1;
        // IntoLuau intoValue13 = (UInt128)1;
        // IntoLuau intoValue14 = (decimal)1;

        intoValue1.Type.ShouldBe(IntoLuau.Kind.Integer);
        intoValue2.Type.ShouldBe(IntoLuau.Kind.Unsigned);
        intoValue3.Type.ShouldBe(IntoLuau.Kind.Integer);
        intoValue4.Type.ShouldBe(IntoLuau.Kind.Unsigned);
        intoValue5.Type.ShouldBe(IntoLuau.Kind.Integer);
        intoValue6.Type.ShouldBe(IntoLuau.Kind.Unsigned);
        intoValue7.Type.ShouldBe(IntoLuau.Kind.Number);
        intoValue8.Type.ShouldBe(IntoLuau.Kind.Number);
        intoValue9.Type.ShouldBe(IntoLuau.Kind.Number);
        intoValue10.Type.ShouldBe(IntoLuau.Kind.Number);
        intoValue11.Type.ShouldBe(IntoLuau.Kind.Number);
    }

    [Fact]
    internal void Into_NullableNumbers()
    {
        IntoLuau intoValue1 = (sbyte?)1;
        IntoLuau intoValue2 = (byte?)1;
        IntoLuau intoValue3 = (short?)1;
        IntoLuau intoValue4 = (ushort?)1;
        IntoLuau intoValue5 = (int?)1;
        IntoLuau intoValue6 = (uint?)1;
        IntoLuau intoValue7 = (long?)1;
        IntoLuau intoValue8 = (ulong?)1;
        IntoLuau intoValue9 = (Half?)1;
        IntoLuau intoValue10 = (float?)1;
        IntoLuau intoValue11 = (double?)1;
        // IntoLuau intoValue = (Int128?)1;
        // IntoLuau intoValue = (UInt128?)1;
        // IntoLuau intoValue = (decimal?)1;

        intoValue1.Type.ShouldBe(IntoLuau.Kind.Integer);
        intoValue2.Type.ShouldBe(IntoLuau.Kind.Unsigned);
        intoValue3.Type.ShouldBe(IntoLuau.Kind.Integer);
        intoValue4.Type.ShouldBe(IntoLuau.Kind.Unsigned);
        intoValue5.Type.ShouldBe(IntoLuau.Kind.Integer);
        intoValue6.Type.ShouldBe(IntoLuau.Kind.Unsigned);
        intoValue7.Type.ShouldBe(IntoLuau.Kind.Number);
        intoValue8.Type.ShouldBe(IntoLuau.Kind.Number);
        intoValue9.Type.ShouldBe(IntoLuau.Kind.Number);
        intoValue10.Type.ShouldBe(IntoLuau.Kind.Number);
        intoValue11.Type.ShouldBe(IntoLuau.Kind.Number);
    }

    [Fact]
    internal void Into_Default()
    {
        IntoLuau intoValue = default;

        intoValue.Type.ShouldBe(IntoLuau.Kind.Nil);
    }

    [Fact]
    internal void Into_Buffer()
    {
        IntoLuau intoValue = default(LuauBuffer);

        intoValue.Type.ShouldBe(IntoLuau.Kind.Nil);
    }

    [Fact]
    internal void Into_UserdataFactory()
    {
        var intoValue = IntoLuau.FromUserdata(new SimpleUserdataType());

        intoValue.Type.ShouldBe(IntoLuau.Kind.UserdataFactory);
    }
}

internal sealed class SimpleUserdataType : ILuauUserdata<SimpleUserdataType>
{
    public static void Register(LuauUserdataRegistry<SimpleUserdataType> registry) { }
}
