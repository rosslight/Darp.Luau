using Shouldly;

namespace Darp.Luau.Tests.Table.Overloads;

public sealed class LuauTableNumberOverloadTests
{
    [Fact]
    public void TryGetNumber_Double_Success() =>
        AssertTryGetNumberSuccess((table, key, out value) => table.TryGetNumber(key, out value), 12.75);

    [Fact]
    public void TryGetNumber_Double_FailureCases_ReturnsFalseAndDefault() =>
        AssertTryGetNumberFailure<double>((table, key, out value) => table.TryGetNumber(key, out value));

    [Fact]
    public void TryGetNumberOrNil_Double_SuccessWithValue() =>
        AssertTryGetNumberOrNilSuccessWithValue(
            (table, key, out value) => table.TryGetNumberOrNil(key, out value),
            12.75
        );

    [Fact]
    public void TryGetNumberOrNil_Double_SuccessWithNil() =>
        AssertTryGetNumberOrNilSuccessWithNil<double>(
            (table, key, out value) => table.TryGetNumberOrNil(key, out value)
        );

    [Fact]
    public void TryGetNumberOrNil_Double_ErrorCase_ReturnsFalseAndNull() =>
        AssertTryGetNumberOrNilErrorCase(
            (LuauTable table, IntoLuau key, out double? value) => table.TryGetNumberOrNil(key, out value)
        );

    [Fact]
    public void TryGetNumber_SByte_Success() =>
        AssertTryGetNumberSuccess((table, key, out value) => table.TryGetNumber(key, out value), (sbyte)12);

    [Fact]
    public void TryGetNumber_SByte_FailureCases_ReturnsFalseAndDefault() =>
        AssertTryGetNumberFailure<sbyte>((table, key, out value) => table.TryGetNumber(key, out value));

    [Fact]
    public void TryGetNumberOrNil_SByte_SuccessWithValue() =>
        AssertTryGetNumberOrNilSuccessWithValue(
            (table, key, out value) => table.TryGetNumberOrNil(key, out value),
            (sbyte)12
        );

    [Fact]
    public void TryGetNumberOrNil_SByte_SuccessWithNil() =>
        AssertTryGetNumberOrNilSuccessWithNil<sbyte>(
            (table, key, out value) => table.TryGetNumberOrNil(key, out value)
        );

    [Fact]
    public void TryGetNumberOrNil_SByte_ErrorCase_ReturnsFalseAndNull() =>
        AssertTryGetNumberOrNilErrorCase<sbyte>((table, key, out value) => table.TryGetNumberOrNil(key, out value));

    [Fact]
    public void TryGetNumber_Byte_Success() =>
        AssertTryGetNumberSuccess((table, key, out value) => table.TryGetNumber(key, out value), (byte)12);

    [Fact]
    public void TryGetNumber_Byte_FailureCases_ReturnsFalseAndDefault() =>
        AssertTryGetNumberFailure<byte>((table, key, out value) => table.TryGetNumber(key, out value));

    [Fact]
    public void TryGetNumberOrNil_Byte_SuccessWithValue() =>
        AssertTryGetNumberOrNilSuccessWithValue<byte>(
            (table, key, out value) => table.TryGetNumberOrNil(key, out value),
            12
        );

    [Fact]
    public void TryGetNumberOrNil_Byte_SuccessWithNil() =>
        AssertTryGetNumberOrNilSuccessWithNil(
            (LuauTable table, IntoLuau key, out byte? value) => table.TryGetNumberOrNil(key, out value)
        );

    [Fact]
    public void TryGetNumberOrNil_Byte_ErrorCase_ReturnsFalseAndNull() =>
        AssertTryGetNumberOrNilErrorCase(
            (LuauTable table, IntoLuau key, out byte? value) => table.TryGetNumberOrNil(key, out value)
        );

    [Fact]
    public void TryGetNumber_Int16_Success() =>
        AssertTryGetNumberSuccess((table, key, out value) => table.TryGetNumber(key, out value), (short)12);

    [Fact]
    public void TryGetNumber_Int16_FailureCases_ReturnsFalseAndDefault() =>
        AssertTryGetNumberFailure(
            (LuauTable table, IntoLuau key, out short value) => table.TryGetNumber(key, out value)
        );

    [Fact]
    public void TryGetNumberOrNil_Int16_SuccessWithValue() =>
        AssertTryGetNumberOrNilSuccessWithValue(
            (table, key, out value) => table.TryGetNumberOrNil(key, out value),
            (short)12
        );

    [Fact]
    public void TryGetNumberOrNil_Int16_SuccessWithNil() =>
        AssertTryGetNumberOrNilSuccessWithNil(
            (LuauTable table, IntoLuau key, out short? value) => table.TryGetNumberOrNil(key, out value)
        );

    [Fact]
    public void TryGetNumberOrNil_Int16_ErrorCase_ReturnsFalseAndNull() =>
        AssertTryGetNumberOrNilErrorCase(
            (LuauTable table, IntoLuau key, out short? value) => table.TryGetNumberOrNil(key, out value)
        );

    [Fact]
    public void TryGetNumber_UInt16_Success() =>
        AssertTryGetNumberSuccess((table, key, out value) => table.TryGetNumber(key, out value), (ushort)12);

    [Fact]
    public void TryGetNumber_UInt16_FailureCases_ReturnsFalseAndDefault() =>
        AssertTryGetNumberFailure(
            (LuauTable table, IntoLuau key, out ushort value) => table.TryGetNumber(key, out value)
        );

    [Fact]
    public void TryGetNumberOrNil_UInt16_SuccessWithValue() =>
        AssertTryGetNumberOrNilSuccessWithValue(
            (table, key, out value) => table.TryGetNumberOrNil(key, out value),
            (ushort)12
        );

    [Fact]
    public void TryGetNumberOrNil_UInt16_SuccessWithNil() =>
        AssertTryGetNumberOrNilSuccessWithNil(
            (LuauTable table, IntoLuau key, out ushort? value) => table.TryGetNumberOrNil(key, out value)
        );

    [Fact]
    public void TryGetNumberOrNil_UInt16_ErrorCase_ReturnsFalseAndNull() =>
        AssertTryGetNumberOrNilErrorCase(
            (LuauTable table, IntoLuau key, out ushort? value) => table.TryGetNumberOrNil(key, out value)
        );

    [Fact]
    public void TryGetNumber_Int32_Success() =>
        AssertTryGetNumberSuccess((table, key, out value) => table.TryGetNumber(key, out value), 12);

    [Fact]
    public void TryGetNumber_Int32_FailureCases_ReturnsFalseAndDefault() =>
        AssertTryGetNumberFailure((LuauTable table, IntoLuau key, out int value) => table.TryGetNumber(key, out value));

    [Fact]
    public void TryGetNumberOrNil_Int32_SuccessWithValue() =>
        AssertTryGetNumberOrNilSuccessWithValue((table, key, out value) => table.TryGetNumberOrNil(key, out value), 12);

    [Fact]
    public void TryGetNumberOrNil_Int32_SuccessWithNil() =>
        AssertTryGetNumberOrNilSuccessWithNil(
            (LuauTable table, IntoLuau key, out int? value) => table.TryGetNumberOrNil(key, out value)
        );

    [Fact]
    public void TryGetNumberOrNil_Int32_ErrorCase_ReturnsFalseAndNull() =>
        AssertTryGetNumberOrNilErrorCase(
            (LuauTable table, IntoLuau key, out int? value) => table.TryGetNumberOrNil(key, out value)
        );

    [Fact]
    public void TryGetNumber_UInt32_Success() =>
        AssertTryGetNumberSuccess((table, key, out value) => table.TryGetNumber(key, out value), (uint)12);

    [Fact]
    public void TryGetNumber_UInt32_FailureCases_ReturnsFalseAndDefault() =>
        AssertTryGetNumberFailure(
            (LuauTable table, IntoLuau key, out uint value) => table.TryGetNumber(key, out value)
        );

    [Fact]
    public void TryGetNumberOrNil_UInt32_SuccessWithValue() =>
        AssertTryGetNumberOrNilSuccessWithValue(
            (table, key, out value) => table.TryGetNumberOrNil(key, out value),
            (uint)12
        );

    [Fact]
    public void TryGetNumberOrNil_UInt32_SuccessWithNil() =>
        AssertTryGetNumberOrNilSuccessWithNil(
            (LuauTable table, IntoLuau key, out uint? value) => table.TryGetNumberOrNil(key, out value)
        );

    [Fact]
    public void TryGetNumberOrNil_UInt32_ErrorCase_ReturnsFalseAndNull() =>
        AssertTryGetNumberOrNilErrorCase(
            (LuauTable table, IntoLuau key, out uint? value) => table.TryGetNumberOrNil(key, out value)
        );

    [Fact]
    public void TryGetNumber_Int64_Success() =>
        AssertTryGetNumberSuccess((table, key, out value) => table.TryGetNumber(key, out value), 12L);

    [Fact]
    public void TryGetNumber_Int64_FailureCases_ReturnsFalseAndDefault() =>
        AssertTryGetNumberFailure(
            (LuauTable table, IntoLuau key, out long value) => table.TryGetNumber(key, out value)
        );

    [Fact]
    public void TryGetNumberOrNil_Int64_SuccessWithValue() =>
        AssertTryGetNumberOrNilSuccessWithValue(
            (table, key, out value) => table.TryGetNumberOrNil(key, out value),
            12L
        );

    [Fact]
    public void TryGetNumberOrNil_Int64_SuccessWithNil() =>
        AssertTryGetNumberOrNilSuccessWithNil(
            (LuauTable table, IntoLuau key, out long? value) => table.TryGetNumberOrNil(key, out value)
        );

    [Fact]
    public void TryGetNumberOrNil_Int64_ErrorCase_ReturnsFalseAndNull() =>
        AssertTryGetNumberOrNilErrorCase(
            (LuauTable table, IntoLuau key, out long? value) => table.TryGetNumberOrNil(key, out value)
        );

    [Fact]
    public void TryGetNumber_UInt64_Success() =>
        AssertTryGetNumberSuccess((table, key, out value) => table.TryGetNumber(key, out value), 12UL);

    [Fact]
    public void TryGetNumber_UInt64_FailureCases_ReturnsFalseAndDefault() =>
        AssertTryGetNumberFailure(
            (LuauTable table, IntoLuau key, out ulong value) => table.TryGetNumber(key, out value)
        );

    [Fact]
    public void TryGetNumberOrNil_UInt64_SuccessWithValue() =>
        AssertTryGetNumberOrNilSuccessWithValue(
            (table, key, out value) => table.TryGetNumberOrNil(key, out value),
            12UL
        );

    [Fact]
    public void TryGetNumberOrNil_UInt64_SuccessWithNil() =>
        AssertTryGetNumberOrNilSuccessWithNil(
            (LuauTable table, IntoLuau key, out ulong? value) => table.TryGetNumberOrNil(key, out value)
        );

    [Fact]
    public void TryGetNumberOrNil_UInt64_ErrorCase_ReturnsFalseAndNull() =>
        AssertTryGetNumberOrNilErrorCase(
            (LuauTable table, IntoLuau key, out ulong? value) => table.TryGetNumberOrNil(key, out value)
        );

    [Fact]
    public void TryGetNumber_Single_Success() =>
        AssertTryGetNumberSuccess((table, key, out value) => table.TryGetNumber(key, out value), 12.75f);

    [Fact]
    public void TryGetNumber_Single_FailureCases_ReturnsFalseAndDefault() =>
        AssertTryGetNumberFailure(
            (LuauTable table, IntoLuau key, out float value) => table.TryGetNumber(key, out value)
        );

    [Fact]
    public void TryGetNumberOrNil_Single_SuccessWithValue() =>
        AssertTryGetNumberOrNilSuccessWithValue(
            (table, key, out value) => table.TryGetNumberOrNil(key, out value),
            12.75f
        );

    [Fact]
    public void TryGetNumberOrNil_Single_SuccessWithNil() =>
        AssertTryGetNumberOrNilSuccessWithNil(
            (LuauTable table, IntoLuau key, out float? value) => table.TryGetNumberOrNil(key, out value)
        );

    [Fact]
    public void TryGetNumberOrNil_Single_ErrorCase_ReturnsFalseAndNull() =>
        AssertTryGetNumberOrNilErrorCase(
            (LuauTable table, IntoLuau key, out float? value) => table.TryGetNumberOrNil(key, out value)
        );

    [Fact]
    public void TryGetNumber_Decimal_Success() =>
        AssertTryGetNumberSuccess((table, key, out value) => table.TryGetNumber(key, out value), 12.75m);

    [Fact]
    public void TryGetNumber_Decimal_FailureCases_ReturnsFalseAndDefault() =>
        AssertTryGetNumberFailure(
            (LuauTable table, IntoLuau key, out decimal value) => table.TryGetNumber(key, out value)
        );

    [Fact]
    public void TryGetNumberOrNil_Decimal_SuccessWithValue() =>
        AssertTryGetNumberOrNilSuccessWithValue(
            (table, key, out value) => table.TryGetNumberOrNil(key, out value),
            12.75m
        );

    [Fact]
    public void TryGetNumberOrNil_Decimal_SuccessWithNil() =>
        AssertTryGetNumberOrNilSuccessWithNil(
            (LuauTable table, IntoLuau key, out decimal? value) => table.TryGetNumberOrNil(key, out value)
        );

    [Fact]
    public void TryGetNumberOrNil_Decimal_ErrorCase_ReturnsFalseAndNull() =>
        AssertTryGetNumberOrNilErrorCase(
            (LuauTable table, IntoLuau key, out decimal? value) => table.TryGetNumberOrNil(key, out value)
        );

    private static void AssertTryGetNumberSuccess<T>(TryGetNumberDelegate<T> tryGetNumber, T expected)
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();
        // The number that is stored is the one that is expected back: a read does not change it.
        table.Set("value", Convert.ToDouble(expected, System.Globalization.CultureInfo.InvariantCulture));

        tryGetNumber(table, "value", out T value).ShouldBeTrue();
        value.ShouldBe(expected);
    }

    private static void AssertTryGetNumberFailure<T>(TryGetNumberDelegate<T> tryGetNumber)
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();

        tryGetNumber(table, "missing", out T missingValue).ShouldBeFalse();
        missingValue.ShouldBe(default!);

        table.Set("wrong", "not a number");
        tryGetNumber(table, "wrong", out T wrongTypeValue).ShouldBeFalse();
        wrongTypeValue.ShouldBe(default!);
    }

    private static void AssertTryGetNumberOrNilSuccessWithValue<T>(
        TryGetNumberOrNilDelegate<T> tryGetNumberOrNil,
        T expected
    )
        where T : struct
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();
        // The number that is stored is the one that is expected back: a read does not change it.
        table.Set("value", Convert.ToDouble(expected, System.Globalization.CultureInfo.InvariantCulture));

        tryGetNumberOrNil(table, "value", out T? value).ShouldBeTrue();
        value.ShouldBe(expected);
    }

    private static void AssertTryGetNumberOrNilSuccessWithNil<T>(TryGetNumberOrNilDelegate<T> tryGetNumberOrNil)
        where T : struct
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();

        tryGetNumberOrNil(table, "missing", out T? value).ShouldBeTrue();
        value.ShouldBeNull();
    }

    private static void AssertTryGetNumberOrNilErrorCase<T>(TryGetNumberOrNilDelegate<T> tryGetNumberOrNil)
        where T : struct
    {
        using var lua = new LuauState();
        LuauTable table = lua.CreateTable();
        table.Set("wrong", "not a number");

        tryGetNumberOrNil(table, "wrong", out T? value).ShouldBeFalse();
        value.ShouldBeNull();
    }

    private delegate bool TryGetNumberDelegate<T>(LuauTable table, IntoLuau key, out T value);

    private delegate bool TryGetNumberOrNilDelegate<T>(LuauTable table, IntoLuau key, out T? value)
        where T : struct;
}
