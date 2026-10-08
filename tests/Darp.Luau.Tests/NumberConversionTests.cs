using Shouldly;

namespace Darp.Luau.Tests;

/// <summary> A Luau number only becomes a managed number that is the same number. </summary>
public sealed class NumberConversionTests : IDisposable
{
    private readonly LuauState _state = new();

    private bool TryGet<T>(string luauNumber, out T value)
        where T : struct, System.Numerics.INumber<T>
    {
        _state.Load($"value = {luauNumber}").Execute();
        return _state.Globals.TryGetNumber("value", out value);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("-0", 0)]
    [InlineData("42", 42)]
    [InlineData("-2147483648", int.MinValue)]
    [InlineData("2147483647", int.MaxValue)]
    public void Int32_ShouldTakeWholeNumbersInItsRange(string luauNumber, int expected)
    {
        TryGet(luauNumber, out int value).ShouldBeTrue();
        value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("-0.25")]
    [InlineData("2147483648")]
    [InlineData("-2147483649")]
    [InlineData("1e30")]
    [InlineData("0/0")]
    [InlineData("math.huge")]
    [InlineData("-math.huge")]
    public void Int32_ShouldRejectWhatItCannotHold(string luauNumber) => TryGet(luauNumber, out int _).ShouldBeFalse();

    [Theory]
    [InlineData("-1")]
    [InlineData("256")]
    [InlineData("0.5")]
    public void Byte_ShouldRejectWhatItCannotHold(string luauNumber) => TryGet(luauNumber, out byte _).ShouldBeFalse();

    [Fact]
    public void UnsignedTypes_ShouldRejectNegativeNumbers()
    {
        TryGet("-1", out ushort _).ShouldBeFalse();
        TryGet("-1", out uint _).ShouldBeFalse();
        TryGet("-1", out ulong _).ShouldBeFalse();
        TryGet("-1", out UInt128 _).ShouldBeFalse();
    }

    [Fact]
    public void WideIntegers_ShouldTakeEveryWholeNumberLuauCanRepresentInTheirRange()
    {
        TryGet("9007199254740992", out long exact).ShouldBeTrue();
        exact.ShouldBe(9007199254740992);
        // Luau has no number between 2^63 - 1024 and 2^63, so the largest long is written as 2^63.
        TryGet("9223372036854775807", out long max).ShouldBeTrue();
        max.ShouldBe(long.MaxValue);
        TryGet("1e19", out long _).ShouldBeFalse();
        TryGet("1e19", out ulong big).ShouldBeTrue();
        big.ShouldBe(10000000000000000000);
        TryGet("1e30", out Int128 wide).ShouldBeTrue();
        ((double)wide).ShouldBe(1e30);
    }

    [Fact]
    public void FloatingPointTypes_ShouldTakeTheNearestValue()
    {
        TryGet("0.1", out float single).ShouldBeTrue();
        single.ShouldBe(0.1f);
        TryGet("0/0", out float notANumber).ShouldBeTrue();
        float.IsNaN(notANumber).ShouldBeTrue();
        TryGet("1e300", out float huge).ShouldBeTrue();
        float.IsPositiveInfinity(huge).ShouldBeTrue();
        TryGet("1.5", out Half half).ShouldBeTrue();
        half.ShouldBe((Half)1.5);
        TryGet("0/0", out double number).ShouldBeTrue();
        double.IsNaN(number).ShouldBeTrue();
    }

    [Fact]
    public void Decimal_ShouldTakeFiniteNumbersInItsRange()
    {
        TryGet("12.75", out decimal value).ShouldBeTrue();
        value.ShouldBe(12.75m);
        TryGet("0.1 + 0.2", out decimal rounded).ShouldBeTrue();
        rounded.ShouldBe(0.3m);
        TryGet("1e30", out decimal _).ShouldBeFalse();
        TryGet("0/0", out decimal _).ShouldBeFalse();
        TryGet("math.huge", out decimal _).ShouldBeFalse();
    }

    [Fact]
    public void NullableRead_ShouldTakeNilAndRejectWhatDoesNotFit()
    {
        _state.Load("fraction = 1.5 whole = 7").Execute();

        _state.Globals.TryGetNumberOrNil("missing", out int? missing).ShouldBeTrue();
        missing.ShouldBeNull();
        _state.Globals.TryGetNumberOrNil("whole", out int? whole).ShouldBeTrue();
        whole.ShouldBe(7);
        _state.Globals.TryGetNumberOrNil("fraction", out int? fraction).ShouldBeFalse();
        fraction.ShouldBeNull();
    }

    [Fact]
    public void LuauValue_ShouldConvertTheSameWay()
    {
        _state.Load("fraction = 1.5 negative = -1").Execute();
        _state.Globals.TryGet("fraction", out LuauValue fraction).ShouldBeTrue();
        _state.Globals.TryGet("negative", out LuauValue negative).ShouldBeTrue();

        fraction.TryGet(out int _).ShouldBeFalse();
        fraction.TryGet(out double asDouble).ShouldBeTrue();
        asDouble.ShouldBe(1.5);
        negative.TryGet(out uint _).ShouldBeFalse();
        negative.TryGet(out sbyte asSByte).ShouldBeTrue();
        asSByte.ShouldBe((sbyte)-1);
    }

    [Fact]
    public void TypedResult_ThatDoesNotFit_ShouldThrow()
    {
        Should.Throw<InvalidCastException>(() => _state.Load("return 1.5").Execute<int>());

        _state.Load("return 2").Execute<int>().ShouldBe(2);
    }

    [Fact]
    public void GeneratedCallback_ShouldRaiseALuauErrorForANumberThatDoesNotFit()
    {
        bool called = false;
        using LuauFunction take = _state.CreateFunction(
            (int count, byte level) =>
            {
                called = true;
                return count + level;
            }
        );
        _state.Globals.Set("take", take);

        (bool ok, string error) = _state
            .Load(
                """
                local ok, err = pcall(take, 1.5, 3)
                return ok, tostring(err)
                """
            )
            .Execute<bool, string>();
        ok.ShouldBeFalse();
        error.ShouldContain("Parameter 1 must be a number that fits Int32 but was 1.5.");
        (ok, error) = _state
            .Load(
                """
                local ok, err = pcall(take, 1, -1)
                return ok, tostring(err)
                """
            )
            .Execute<bool, string>();
        ok.ShouldBeFalse();
        error.ShouldContain("Parameter 2 must be a number that fits Byte but was -1.");
        called.ShouldBeFalse();

        _state.Load("return take(40, 2)").Execute<int>().ShouldBe(42);
    }

    [Fact]
    public void GeneratedCallback_ShouldReadAnEnumAsItsUnderlyingType()
    {
        using LuauFunction name = _state.CreateFunction(
            (StrictLevel level, StrictLevel? fallback) => $"{level}/{fallback}"
        );
        _state.Globals.Set("name", name);

        _state.Load("return name(2, nil)").Execute<string>().ShouldBe("High/");
        // A value without a name is still a value of the enum. Whether it is allowed is up to the callback.
        _state.Load("return name(7, 1)").Execute<string>().ShouldBe("7/Low");
        // The enum is stored in a byte, which holds neither of these.
        _state.Load("return (pcall(name, 300, nil))").Execute<bool>().ShouldBeFalse();
        _state.Load("return (pcall(name, 1.5, nil))").Execute<bool>().ShouldBeFalse();
        _state.Load("return (pcall(name, 1, -1))").Execute<bool>().ShouldBeFalse();
    }

    public void Dispose() => _state.Dispose();
}

public enum StrictLevel : byte
{
    Low = 1,
    High = 2,
}
