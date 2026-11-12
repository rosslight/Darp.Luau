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
        return _state.Globals.TryGet("value", out value);
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
        TryGet("-9223372036854775808", out long min).ShouldBeTrue();
        min.ShouldBe(long.MinValue);
        TryGet("1e19", out ulong big).ShouldBeTrue();
        big.ShouldBe(10000000000000000000);
        TryGet("1e30", out Int128 wide).ShouldBeTrue();
        ((double)wide).ShouldBe(1e30);
        TryGet("1e30", out System.Numerics.BigInteger unbounded).ShouldBeTrue();
        ((double)unbounded).ShouldBe(1e30);
    }

    [Fact]
    public void WideIntegers_ShouldRejectTheFirstNumberAboveTheirRange()
    {
        // Luau reads the literal 9223372036854775807 as 2^63: a double has no number in between. A long cannot hold
        // 2^63, and saturating to its largest value would be a different number.
        TryGet("9223372036854775807", out long _).ShouldBeFalse();
        TryGet("9223372036854775808", out long _).ShouldBeFalse();
        TryGet("9223372036854775808", out nint _).ShouldBeFalse();
        TryGet("18446744073709551616", out ulong _).ShouldBeFalse();
        TryGet("2^127", out Int128 _).ShouldBeFalse();
        TryGet("2^128", out UInt128 _).ShouldBeFalse();
        TryGet("1e19", out long _).ShouldBeFalse();

        TryGet("9223372036854775808", out ulong above).ShouldBeTrue();
        above.ShouldBe(9223372036854775808);
    }

    [Fact]
    public void IntegerTypes_ShouldRejectNaNAndInfinityWithoutThrowing()
    {
        TryGet("math.huge", out System.Numerics.BigInteger _).ShouldBeFalse();
        TryGet("-math.huge", out System.Numerics.BigInteger _).ShouldBeFalse();
        TryGet("0/0", out System.Numerics.BigInteger _).ShouldBeFalse();
        TryGet("math.huge", out UInt128 _).ShouldBeFalse();
        TryGet("0/0", out long _).ShouldBeFalse();
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
        TryGet("0/0", out System.Runtime.InteropServices.NFloat native).ShouldBeTrue();
        System.Runtime.InteropServices.NFloat.IsNaN(native).ShouldBeTrue();
    }

    [Fact]
    public void Decimal_ShouldTakeFiniteNumbersInItsRangeExactly()
    {
        TryGet("12.75", out decimal value).ShouldBeTrue();
        value.ShouldBe(12.75m);
        TryGet("0.1", out decimal rounded).ShouldBeTrue();
        rounded.ShouldBe(0.1000000000000000055511151231m);
        TryGet("1e-30", out decimal tiny).ShouldBeTrue();
        tiny.ShouldBe(0m);
        TryGet("1e30", out decimal _).ShouldBeFalse();
        TryGet("0/0", out decimal _).ShouldBeFalse();
        TryGet("math.huge", out decimal _).ShouldBeFalse();
    }

#if NET11_0_OR_GREATER
    /// <summary> .NET 10 converts with <see cref="Internal.LuauNumber.ToDecimalExactly"/>, which has to agree with the cast of .NET 11. </summary>
    [Fact]
    public void Decimal_ShouldConvertOnEveryFrameworkLikeTheCastOfNet11()
    {
        var random = new Random(11);
        for (int i = 0; i < 200_000; i++)
        {
            double number = (i % 4) switch
            {
                0 => BitConverter.Int64BitsToDouble(random.NextInt64()),
                1 => (random.NextDouble() - 0.5) * Math.Pow(10, random.Next(-35, 29)),
                2 => Math.Round(random.NextDouble() * 1e6, random.Next(0, 8)),
                // A small odd significand far below the decimal point: the dropped digits are often exactly a half.
                _ => (2 * random.Next(0, 1 << 20) + 1) * Math.Pow(2, -random.Next(25, 40)),
            };
            if (!(Math.Abs(number) < 79228162514264337593543950336d))
                continue;

            decimal.GetBits(Internal.LuauNumber.ToDecimalExactly(number))
                .ShouldBe(decimal.GetBits((decimal)number), $"{number:R}");
        }
    }
#endif

    [Fact]
    public void NullableRead_ShouldTakeNilAndRejectWhatDoesNotFit()
    {
        _state.Load("fraction = 1.5 whole = 7").Execute();

        _state.Globals.TryGet("missing", out int? missing).ShouldBeTrue();
        missing.ShouldBeNull();
        _state.Globals.TryGet("whole", out int? whole).ShouldBeTrue();
        whole.ShouldBe(7);
        _state.Globals.TryGet("fraction", out int? fraction).ShouldBeFalse();
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
        Should.Throw<LuaGetException>(() => _state.Load("return 1.5").Execute<int>());

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
        error.ShouldBe("invalid argument #1 (number that fits Int32 expected, got 1.5)");
        (ok, error) = _state
            .Load(
                """
                local ok, err = pcall(take, 1, -1)
                return ok, tostring(err)
                """
            )
            .Execute<bool, string>();
        ok.ShouldBeFalse();
        error.ShouldBe("invalid argument #2 (number that fits Byte expected, got -1)");
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
