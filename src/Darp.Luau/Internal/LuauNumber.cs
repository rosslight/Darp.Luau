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

    // 2^63: below this magnitude, converting to an integer type and back tells exactly whether the type holds the
    // number. From here on the widest types saturate at an end that rounds back to the same double.
    private const double ExactRoundTripLimit = 9223372036854775808d;

    /// <summary> Converts <paramref name="number"/> to <typeparamref name="T"/> if <typeparamref name="T"/> can hold it. </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item> An integer type holds whole numbers in its range. <c>1.5</c>, <c>-1</c> for an unsigned type, NaN and infinity fail. </item>
    /// <item> A floating-point type takes the nearest value it has, which may be infinity. </item>
    /// <item> <see cref="decimal"/> takes every finite number in its range, rounded to its precision. </item>
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
            value = T.CreateTruncating(number);
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

    /// <summary> The error for a number that <typeparamref name="T"/> cannot hold. </summary>
    /// <param name="label">What was read, such as <c>Parameter 2</c>.</param>
    /// <param name="number">The number that was found.</param>
    public static string DescribeMismatch<T>(string label, double number)
        where T : struct, INumber<T> =>
        $"{label} must be a number that fits {typeof(T).Name} but was {number.ToString(System.Globalization.CultureInfo.InvariantCulture)}.";
}
