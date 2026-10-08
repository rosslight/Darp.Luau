using System.Numerics;

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

    /// <summary> Converts <paramref name="number"/> to <typeparamref name="T"/> if <typeparamref name="T"/> can hold it. </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item> An integer type holds whole numbers in its range. <c>1.5</c>, <c>-1</c> for an unsigned type, NaN and infinity fail. </item>
    /// <item> <see cref="float"/> and <see cref="Half"/> take the nearest value they have, which may be infinity. </item>
    /// <item> <see cref="decimal"/> takes every finite number in its range, rounded to its precision. </item>
    /// </list>
    /// </remarks>
    public static bool TryConvert<T>(double number, out T value)
        where T : struct, INumber<T>
    {
        if (typeof(T) == typeof(double) || typeof(T) == typeof(float) || typeof(T) == typeof(Half))
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
        // range it saturates, a fraction is cut off and NaN becomes zero, and none of those lead back.
        value = T.CreateSaturating(number);
        return double.CreateTruncating(value) == number;
    }

    /// <summary> The error for a number that <typeparamref name="T"/> cannot hold. </summary>
    /// <param name="label">What was read, such as <c>Parameter 2</c>.</param>
    /// <param name="number">The number that was found.</param>
    public static string DescribeMismatch<T>(string label, double number)
        where T : struct, INumber<T> =>
        $"{label} must be a number that fits {typeof(T).Name} but was {number.ToString(System.Globalization.CultureInfo.InvariantCulture)}.";
}
