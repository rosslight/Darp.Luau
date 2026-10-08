using System.Text;
using Shouldly;

namespace Darp.Luau.Tests.Table.Overloads;

public sealed class LuauTablePrimitiveOverloadTests
{
    [Fact]
    public void TryGetBoolean_Success()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();
        table.Set("value", true);

        table.TryGetBoolean("value", out bool value).ShouldBeTrue();
        value.ShouldBe(true);
    }

    [Fact]
    public void TryGetBoolean_FailureCases_ReturnsFalseAndDefault()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();

        table.TryGetBoolean("missing", out bool missingValue).ShouldBeFalse();
        missingValue.ShouldBeFalse();

        table.Set("wrong", 1);
        table.TryGetBoolean("wrong", out bool wrongTypeValue).ShouldBeFalse();
        wrongTypeValue.ShouldBeFalse();
    }

    [Fact]
    public void TryGetBooleanOrNil_SuccessWithValue()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();
        table.Set("value", true);

        table.TryGetBooleanOrNil("value", out bool? value).ShouldBeTrue();
        value.ShouldBe(true);
    }

    [Fact]
    public void TryGetBooleanOrNil_SuccessWithNil()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();

        table.TryGetBooleanOrNil("missing", out bool? value).ShouldBeTrue();
        value.ShouldBeNull();
    }

    [Fact]
    public void TryGetBooleanOrNil_ErrorCase_ReturnsFalseAndNull()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();
        table.Set("wrong", 1);

        table.TryGetBooleanOrNil("wrong", out bool? value).ShouldBeFalse();
        value.ShouldBeNull();
    }

    [Fact]
    public void GetBooleanOrNil_SuccessWithValue()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();
        table.Set("value", true);

        table.GetBooleanOrNil("value").ShouldBe(true);
    }

    [Fact]
    public void GetBooleanOrNil_SuccessWithNil()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();

        table.GetBooleanOrNil("missing").ShouldBeNull();
    }

    [Fact]
    public void GetBooleanOrNil_ErrorCase_ThrowsException()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();
        table.Set("wrong", 1);

        Should.Throw<Exception>(() => table.GetBooleanOrNil("wrong"));
    }

    [Fact]
    public void TryGetUtf8String_Bytes_Success()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();
        table.Set("value", "hallo");

        LuauMarshal.TryGetUtf8StringSpan(table, "value", out ReadOnlySpan<byte> bytes).ShouldBeTrue();
        Encoding.UTF8.GetString(bytes).ShouldBe("hallo");
    }

    [Fact]
    public void TryGetUtf8String_Bytes_FailureCases_ReturnsFalseAndDefault()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();

        LuauMarshal.TryGetUtf8StringSpan(table, "missing", out ReadOnlySpan<byte> missingValue).ShouldBeFalse();
        missingValue.IsEmpty.ShouldBeTrue();

        table.Set("wrong", 1);
        LuauMarshal.TryGetUtf8StringSpan(table, "wrong", out ReadOnlySpan<byte> wrongTypeValue).ShouldBeFalse();
        wrongTypeValue.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void TryGetUtf8StringOrNil_Bytes_SuccessWithValue()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();
        table.Set("value", "hallo");

        LuauMarshal
            .TryGetUtf8StringSpanOrNil(table, "value", out ReadOnlySpan<byte> value, out bool isNil)
            .ShouldBeTrue();
        isNil.ShouldBeFalse();
        Encoding.UTF8.GetString(value).ShouldBe("hallo");
    }

    [Fact]
    public void TryGetUtf8StringOrNil_Bytes_SuccessWithNil()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();

        LuauMarshal
            .TryGetUtf8StringSpanOrNil(table, "missing", out ReadOnlySpan<byte> value, out bool isNil)
            .ShouldBeTrue();
        isNil.ShouldBeTrue();
        value.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void TryGetUtf8StringOrNil_Bytes_ErrorCase_ReturnsFalseAndDefault()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();
        table.Set("wrong", 1);

        LuauMarshal
            .TryGetUtf8StringSpanOrNil(table, "wrong", out ReadOnlySpan<byte> value, out bool isNil)
            .ShouldBeFalse();
        isNil.ShouldBeFalse();
        value.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void TryGetUtf8String_String_Success()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();
        table.Set("value", "hallo");

        table.TryGetUtf8String("value", out string? value).ShouldBeTrue();
        value.ShouldBe("hallo");
    }

    [Fact]
    public void TryGetUtf8String_String_FailureCases_ReturnsFalseAndNull()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();

        table.TryGetUtf8String("missing", out string? missingValue).ShouldBeFalse();
        missingValue.ShouldBeNull();

        table.Set("wrong", 1);
        table.TryGetUtf8String("wrong", out string? wrongTypeValue).ShouldBeFalse();
        wrongTypeValue.ShouldBeNull();
    }

    [Fact]
    public void TryGetUtf8StringOrNil_String_SuccessWithValue()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();
        table.Set("value", "hallo");

        table.TryGetUtf8StringOrNil("value", out string? value).ShouldBeTrue();
        value.ShouldBe("hallo");
    }

    [Fact]
    public void TryGetUtf8StringOrNil_String_SuccessWithNil()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();

        table.TryGetUtf8StringOrNil("missing", out string? value).ShouldBeTrue();
        value.ShouldBeNull();
    }

    [Fact]
    public void TryGetUtf8StringOrNil_String_ErrorCase_ReturnsFalseAndNull()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();
        table.Set("wrong", 1);

        table.TryGetUtf8StringOrNil("wrong", out string? value).ShouldBeFalse();
        value.ShouldBeNull();
    }

    [Fact]
    public void GetUtf8String_Success()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();
        table.Set("value", "hallo");

        table.GetUtf8String("value").ShouldBe("hallo");
    }

    [Fact]
    public void GetUtf8String_ErrorCase_ThrowsException()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();
        table.Set("wrong", 1);

        Should.Throw<Exception>(() => table.GetUtf8String("wrong"));
    }

    [Fact]
    public void GetUtf8StringOrNil_SuccessWithValue()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();
        table.Set("value", "hallo");

        table.GetUtf8StringOrNil("value").ShouldBe("hallo");
    }

    [Fact]
    public void GetUtf8StringOrNil_SuccessWithNil()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();

        table.GetUtf8StringOrNil("missing").ShouldBeNull();
    }

    [Fact]
    public void GetUtf8StringOrNil_ErrorCase_ThrowsException()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();
        table.Set("wrong", 1);

        Should.Throw<Exception>(() => table.GetUtf8StringOrNil("wrong"));
    }

    [Fact]
    public void TryGetBuffer_Bytes_Success()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();
        byte[] expected = [0x01, 0x02, 0x03];
        table.Set("value", expected);

        LuauMarshal.TryGetBufferSpan(table, "value", out ReadOnlySpan<byte> value).ShouldBeTrue();
        value.ToArray().ShouldBe(expected);
    }

    [Fact]
    public void TryGetBuffer_Bytes_FailureCases_ReturnsFalseAndDefault()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();

        LuauMarshal.TryGetBufferSpan(table, "missing", out ReadOnlySpan<byte> missingValue).ShouldBeFalse();
        missingValue.IsEmpty.ShouldBeTrue();

        table.Set("wrong", 1);
        LuauMarshal.TryGetBufferSpan(table, "wrong", out ReadOnlySpan<byte> wrongTypeValue).ShouldBeFalse();
        wrongTypeValue.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void TryGetBufferOrNil_Bytes_SuccessWithValue()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();
        byte[] expected = [0x01, 0x02, 0x03];
        table.Set("value", expected);

        LuauMarshal.TryGetBufferSpanOrNil(table, "value", out ReadOnlySpan<byte> value, out bool isNil).ShouldBeTrue();
        isNil.ShouldBeFalse();
        value.ToArray().ShouldBe(expected);
    }

    [Fact]
    public void TryGetBufferOrNil_Bytes_SuccessWithNil()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();

        LuauMarshal
            .TryGetBufferSpanOrNil(table, "missing", out ReadOnlySpan<byte> value, out bool isNil)
            .ShouldBeTrue();
        isNil.ShouldBeTrue();
        value.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void TryGetBufferOrNil_Bytes_ErrorCase_ReturnsFalseAndDefault()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();
        table.Set("wrong", 1);

        LuauMarshal.TryGetBufferSpanOrNil(table, "wrong", out ReadOnlySpan<byte> value, out bool isNil).ShouldBeFalse();
        isNil.ShouldBeFalse();
        value.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void TryGetBuffer_Array_Success()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();
        byte[] expected = [0x01, 0x02, 0x03];
        table.Set("value", expected);

        table.TryGetBuffer("value", out byte[]? value).ShouldBeTrue();
        value.ShouldNotBeNull();
        value.ShouldBe(expected);
    }

    [Fact]
    public void TryGetBuffer_Array_FailureCases_ReturnsFalseAndNull()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();

        table.TryGetBuffer("missing", out byte[]? missingValue).ShouldBeFalse();
        missingValue.ShouldBeNull();

        table.Set("wrong", 1);
        table.TryGetBuffer("wrong", out byte[]? wrongTypeValue).ShouldBeFalse();
        wrongTypeValue.ShouldBeNull();
    }

    [Fact]
    public void TryGetBufferOrNil_Array_SuccessWithValue()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();
        byte[] expected = [0x01, 0x02, 0x03];
        table.Set("value", expected);

        table.TryGetBufferOrNil("value", out byte[]? value).ShouldBeTrue();
        value.ShouldNotBeNull();
        value.ShouldBe(expected);
    }

    [Fact]
    public void TryGetBufferOrNil_Array_SuccessWithNil()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();

        table.TryGetBufferOrNil("missing", out byte[]? value).ShouldBeTrue();
        value.ShouldBeNull();
    }

    [Fact]
    public void TryGetBufferOrNil_Array_ErrorCase_ReturnsFalseAndNull()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();
        table.Set("wrong", 1);

        table.TryGetBufferOrNil("wrong", out byte[]? value).ShouldBeFalse();
        value.ShouldBeNull();
    }

    [Fact]
    public void GetBuffer_Success()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();
        byte[] expected = [0x01, 0x02, 0x03];
        table.Set("value", expected);

        table.GetBuffer("value").ShouldBe(expected);
    }

    [Fact]
    public void GetBuffer_ErrorCase_ThrowsException()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();
        table.Set("wrong", 1);

        Should.Throw<Exception>(() => table.GetBuffer("wrong"));
    }

    [Fact]
    public void GetBufferOrNil_SuccessWithValue()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();
        byte[] expected = [0x01, 0x02, 0x03];
        table.Set("value", expected);

        table.GetBufferOrNil("value").ShouldBe(expected);
    }

    [Fact]
    public void GetBufferOrNil_SuccessWithNil()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();

        table.GetBufferOrNil("missing").ShouldBeNull();
    }

    [Fact]
    public void GetBufferOrNil_ErrorCase_ThrowsException()
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();
        table.Set("wrong", 1);

        Should.Throw<Exception>(() => table.GetBufferOrNil("wrong"));
    }
}
