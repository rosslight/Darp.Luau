using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Darp.Luau.Internal;

/// <summary> The one place that turns a Luau number into a managed numeric type. </summary>
/// <remarks>
/// A Luau number is a <see cref="double"/>. A conversion must not change the number without the caller noticing, so
/// it fails where a cast would truncate, wrap or saturate.
/// </remarks>
internal static class LuauNumber
{
    // 2^96: the first number above the range of decimal.
    private const double DecimalLimit = 79228162514264337593543950336d;

    private const int MaxDecimalScale = 28;
    private static readonly BigInteger DecimalMantissaLimit = new(DecimalLimit);

    // 2^63: below this magnitude, converting to an integer type and back tells exactly whether the type holds the
    // number. From here on the widest types saturate at an end that rounds back to the same double.
    private const double ExactRoundTripLimit = 9223372036854775808d;

    /// <summary> Converts <paramref name="number"/> to <typeparamref name="T"/> if <typeparamref name="T"/> can hold it. </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item> An integer type holds whole numbers in its range. <c>1.5</c>, <c>-1</c> for an unsigned type, NaN and infinity fail. </item>
    /// <item> A floating-point type takes the nearest value it has, which may be infinity. </item>
    /// <item> <see cref="decimal"/> takes every finite number in its range. See <see cref="ToDecimal"/>. </item>
    /// </list>
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryConvert<T>(double number, out T value)
        where T : struct, INumber<T>
    {
        if (
            typeof(T) == typeof(double)
            || typeof(T) == typeof(float)
            || typeof(T) == typeof(Half)
            || typeof(T) == typeof(NFloat)
        )
        {
            value = T.CreateTruncating(number);
            return true;
        }

        if (typeof(T) == typeof(decimal))
        {
            // False for NaN as well.
            if (!(Math.Abs(number) < DecimalLimit))
            {
                value = default;
                return false;
            }
            value = T.CreateTruncating(ToDecimal(number));
            return true;
        }

        // An integer type holds the number exactly when the way there and back ends at the same number. Out of
        // range it saturates and a fraction is cut off, and neither leads back.
        if (Math.Abs(number) < ExactRoundTripLimit)
        {
            value = T.CreateSaturating(number);
            return double.CreateTruncating(value) == number;
        }

        return TryConvertHuge(number, out value);
    }

    /// <summary> Converts a number of at least 2^63 in magnitude, NaN or infinity to an integer type. </summary>
    /// <remarks>
    /// Out of line, because few numbers get here and only the widest types hold any of them. Every finite double
    /// this large is a whole number, so the checked conversion fails exactly when it is out of range.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TryConvertHuge<T>(double number, out T value)
        where T : struct, INumber<T>
    {
        if (double.IsFinite(number))
        {
            try
            {
                value = T.CreateChecked(number);
                return true;
            }
            catch (OverflowException) { }
        }

        value = default;
        return false;
    }

    /// <summary> Converts a finite number below 2^96 in magnitude to the decimal of the same value. </summary>
    /// <remarks>
    /// A number with more digits than a decimal keeps is rounded half to even, like <c>0.1</c> to
    /// <c>0.1000000000000000055511151231</c>. Since .NET 11 the cast does this; before, it rounded to 15 significant
    /// digits. A host gets the same value on every target framework.
    /// </remarks>
    public static decimal ToDecimal(double number)
    {
#if NET11_0_OR_GREATER
        return (decimal)number;
#else
        return ToDecimalExactly(number);
#endif
    }

    /// <summary> What the cast to decimal does since .NET 11. </summary>
    /// <remarks> Compiled for every target framework, so that the tests on .NET 11 can compare it with the cast. </remarks>
    internal static decimal ToDecimalExactly(double number)
    {
        ulong bits = BitConverter.DoubleToUInt64Bits(number);
        int biasedExponent = (int)(bits >> 52) & 0x7FF;
        ulong significand = bits & 0xF_FFFF_FFFF_FFFF;
        // A subnormal number has no implicit leading bit and the exponent of the smallest normal number.
        if (biasedExponent == 0)
            biasedExponent = 1;
        else
            significand |= 1UL << 52;

        // The magnitude is significand * 2^-shift.
        int shift = 1075 - biasedExponent;
        if (shift <= 0)
            return CreateDecimal(new BigInteger(significand) << -shift, scale: 0, number);

        int zeros = Math.Min(BitOperations.TrailingZeroCount(significand), shift);
        significand >>= zeros;
        shift -= zeros;

        // significand / 2^shift = significand * 5^shift / 10^shift. The significand is odd now, so no smaller scale
        // holds the number exactly.
        BigInteger exact = significand * BigInteger.Pow(5, shift);
        if (shift <= MaxDecimalScale && exact < DecimalMantissaLimit)
            return CreateDecimal(exact, shift, number);

        // Keep as many places as fit, rounding once from the exact value.
        for (int scale = Math.Min(MaxDecimalScale, shift - 1); ; scale--)
        {
            BigInteger divisor = BigInteger.Pow(10, shift - scale);
            BigInteger mantissa = BigInteger.DivRem(exact, divisor, out BigInteger remainder);
            int half = (remainder * 2).CompareTo(divisor);
            if (half > 0 || (half == 0 && !mantissa.IsEven))
                mantissa++;
            if (mantissa < DecimalMantissaLimit)
                return CreateDecimal(mantissa, scale, number);
        }
    }

    /// <summary> The decimal <paramref name="mantissa"/> * 10^-<paramref name="scale"/> with the sign of <paramref name="number"/>. </summary>
    private static decimal CreateDecimal(BigInteger mantissa, int scale, double number)
    {
        // Like the cast, a number that rounds to zero becomes a plain zero.
        if (mantissa.IsZero)
            return 0m;
        var value = (UInt128)mantissa;
        return new decimal(
            lo: (int)(ulong)value,
            mid: (int)((ulong)value >> 32),
            hi: (int)(ulong)(value >> 64),
            isNegative: double.IsNegative(number),
            scale: (byte)scale
        );
    }

    /// <summary> The error for a number that <typeparamref name="T"/> cannot hold. </summary>
    /// <param name="label">What was read, such as <c>Parameter 2</c>.</param>
    /// <param name="number">The number that was found.</param>
    public static string DescribeMismatch<T>(string label, double number)
        where T : struct, INumber<T> =>
        $"{label} must be a number that fits {typeof(T).Name} but was {number.ToString(System.Globalization.CultureInfo.InvariantCulture)}.";
}
