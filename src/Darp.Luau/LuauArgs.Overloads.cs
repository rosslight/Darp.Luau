using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text;
using Darp.Luau.Internal;

namespace Darp.Luau;

public readonly ref partial struct LuauArgs
{
    /// <summary>
    /// Attempts to read the parameter at <paramref name="parameterIndex"/> as a Lua number that
    /// <typeparamref name="T"/> can hold.
    /// </summary>
    /// <param name="parameterIndex">1-based parameter index in the range <c>1..ArgumentCount</c>.</param>
    /// <param name="value">Receives the number when the read succeeds.</param>
    /// <param name="error">Receives a descriptive error when the read fails.</param>
    /// <typeparam name="T">The numeric type to read, such as <see cref="int"/> or <see cref="float"/>.</typeparam>
    /// <returns><c>true</c> when the parameter is a number that <typeparamref name="T"/> can hold; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// An integer type only takes whole numbers in its range: <c>1.5</c>, <c>-1</c> for an unsigned type, NaN and
    /// infinity fail instead of being truncated or wrapped. <see cref="float"/> and <see cref="Half"/> take the
    /// nearest value they have. <see cref="decimal"/> takes every finite number in its range.
    /// </remarks>
    public bool TryReadNumber<T>(int parameterIndex, out T value, [NotNullWhen(false)] out string? error)
        where T : struct, INumber<T>
    {
        value = default;
        if (!TryReadNumber(parameterIndex, out double number, out error))
            return false;
        if (LuauNumber.TryConvert(number, out value))
            return true;

        error = LuauNumber.DescribeMismatch<T>($"Parameter {parameterIndex}", number);
        return false;
    }

    /// <summary>
    /// Attempts to read the parameter at <paramref name="parameterIndex"/> as a Lua number that
    /// <typeparamref name="T"/> can hold, or as <c>nil</c>.
    /// </summary>
    /// <param name="parameterIndex">1-based parameter index in the range <c>1..ArgumentCount</c>.</param>
    /// <param name="value">Receives the number, or <c>null</c> when the parameter is <c>nil</c>.</param>
    /// <param name="error">Receives a descriptive error when the read fails.</param>
    /// <typeparam name="T">The numeric type to read, such as <see cref="int"/> or <see cref="float"/>.</typeparam>
    /// <returns><c>true</c> when the parameter is <c>nil</c> or a number that <typeparamref name="T"/> can hold; otherwise <c>false</c>.</returns>
    /// <remarks> See <see cref="TryReadNumber{T}(int, out T, out string)"/> for what a type can hold. </remarks>
    public bool TryReadNumberOrNil<T>(int parameterIndex, out T? value, [NotNullWhen(false)] out string? error)
        where T : struct, INumber<T>
    {
        value = null;
        if (!TryReadNumberOrNil(parameterIndex, out double? number, out error))
            return false;
        if (number is not { } present)
            return true;
        if (LuauNumber.TryConvert(present, out T converted))
        {
            value = converted;
            return true;
        }

        error = LuauNumber.DescribeMismatch<T>($"Parameter {parameterIndex}", present);
        return false;
    }

    /// <inheritdoc cref="TryReadUtf8String(int, out ReadOnlySpan{byte}, out string)"/>
    /// <remarks>Decoding creates a managed <see cref="string"/> instance.</remarks>
    public bool TryReadUtf8String(
        int parameterIndex,
        [NotNullWhen(true)] out string? value,
        [NotNullWhen(false)] out string? error
    )
    {
        value = null;
        if (!TryReadUtf8String(parameterIndex, out ReadOnlySpan<byte> rawValue, out error))
            return false;

        value = Encoding.UTF8.GetString(rawValue);
        return true;
    }

    /// <inheritdoc cref="TryReadUtf8StringOrNil(int, out ReadOnlySpan{byte}, out bool, out string)"/>
    /// <remarks>
    /// Decoding creates a managed <see cref="string"/> instance. When the Lua value is <c>nil</c>,
    /// <paramref name="value"/> is set to <c>null</c>.
    /// </remarks>
    public bool TryReadUtf8StringOrNil(int parameterIndex, out string? value, [NotNullWhen(false)] out string? error)
    {
        value = null;
        if (!TryReadUtf8StringOrNil(parameterIndex, out ReadOnlySpan<byte> rawValue, out bool isNil, out error))
            return false;

        value = isNil ? null : Encoding.UTF8.GetString(rawValue);
        return true;
    }

    /// <inheritdoc cref="TryReadBuffer(int, out ReadOnlySpan{byte}, out string)"/>
    /// <remarks>Copies Lua buffer content into a new managed <see cref="byte"/> array.</remarks>
    public bool TryReadBuffer(
        int parameterIndex,
        [NotNullWhen(true)] out byte[]? value,
        [NotNullWhen(false)] out string? error
    )
    {
        value = null;
        if (!TryReadBuffer(parameterIndex, out ReadOnlySpan<byte> rawValue, out error))
            return false;

        value = rawValue.ToArray();
        return true;
    }

    /// <inheritdoc cref="TryReadBufferOrNil(int, out ReadOnlySpan{byte}, out bool, out string)"/>
    /// <remarks>
    /// Copies Lua buffer content into a new managed <see cref="byte"/> array.
    /// When the Lua value is <c>nil</c>, <paramref name="value"/> is set to <c>null</c>.
    /// </remarks>
    public bool TryReadBufferOrNil(int parameterIndex, out byte[]? value, [NotNullWhen(false)] out string? error)
    {
        value = null;
        if (!TryReadBufferOrNil(parameterIndex, out ReadOnlySpan<byte> rawValue, out bool isNil, out error))
            return false;

        value = isNil ? null : rawValue.ToArray();
        return true;
    }
}
