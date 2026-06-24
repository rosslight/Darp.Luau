using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Darp.Luau;

public readonly partial struct LuauArgs
{
    /// <inheritdoc cref="TryReadNumber(int, out double, out string)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryReadNumber(int parameterIndex, out sbyte value, [NotNullWhen(false)] out string? error)
    {
        value = 0;
        if (!TryReadNumber(parameterIndex, out double rawValue, out error))
            return false;

        value = (sbyte)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryReadNumberOrNil(int, out double?, out string)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryReadNumberOrNil(int parameterIndex, out sbyte? value, [NotNullWhen(false)] out string? error)
    {
        value = null;
        if (!TryReadNumberOrNil(parameterIndex, out double? rawValue, out error))
            return false;

        value = (sbyte?)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryReadNumber(int, out double, out string)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryReadNumber(int parameterIndex, out byte value, [NotNullWhen(false)] out string? error)
    {
        value = 0;
        if (!TryReadNumber(parameterIndex, out double rawValue, out error))
            return false;

        value = (byte)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryReadNumberOrNil(int, out double?, out string)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryReadNumberOrNil(int parameterIndex, out byte? value, [NotNullWhen(false)] out string? error)
    {
        value = null;
        if (!TryReadNumberOrNil(parameterIndex, out double? rawValue, out error))
            return false;

        value = (byte?)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryReadNumber(int, out double, out string)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryReadNumber(int parameterIndex, out short value, [NotNullWhen(false)] out string? error)
    {
        value = 0;
        if (!TryReadNumber(parameterIndex, out double rawValue, out error))
            return false;

        value = (short)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryReadNumberOrNil(int, out double?, out string)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryReadNumberOrNil(int parameterIndex, out short? value, [NotNullWhen(false)] out string? error)
    {
        value = null;
        if (!TryReadNumberOrNil(parameterIndex, out double? rawValue, out error))
            return false;

        value = (short?)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryReadNumber(int, out double, out string)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryReadNumber(int parameterIndex, out ushort value, [NotNullWhen(false)] out string? error)
    {
        value = 0;
        if (!TryReadNumber(parameterIndex, out double rawValue, out error))
            return false;

        value = (ushort)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryReadNumberOrNil(int, out double?, out string)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryReadNumberOrNil(int parameterIndex, out ushort? value, [NotNullWhen(false)] out string? error)
    {
        value = null;
        if (!TryReadNumberOrNil(parameterIndex, out double? rawValue, out error))
            return false;

        value = (ushort?)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryReadNumber(int, out double, out string)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryReadNumber(int parameterIndex, out int value, [NotNullWhen(false)] out string? error)
    {
        value = 0;
        if (!TryReadNumber(parameterIndex, out double rawValue, out error))
            return false;

        value = (int)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryReadNumberOrNil(int, out double?, out string)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryReadNumberOrNil(int parameterIndex, out int? value, [NotNullWhen(false)] out string? error)
    {
        value = null;
        if (!TryReadNumberOrNil(parameterIndex, out double? rawValue, out error))
            return false;

        value = (int?)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryReadNumber(int, out double, out string)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryReadNumber(int parameterIndex, out uint value, [NotNullWhen(false)] out string? error)
    {
        value = 0;
        if (!TryReadNumber(parameterIndex, out double rawValue, out error))
            return false;

        value = (uint)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryReadNumberOrNil(int, out double?, out string)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryReadNumberOrNil(int parameterIndex, out uint? value, [NotNullWhen(false)] out string? error)
    {
        value = null;
        if (!TryReadNumberOrNil(parameterIndex, out double? rawValue, out error))
            return false;

        value = (uint?)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryReadNumber(int, out double, out string)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryReadNumber(int parameterIndex, out long value, [NotNullWhen(false)] out string? error)
    {
        value = 0;
        if (!TryReadNumber(parameterIndex, out double rawValue, out error))
            return false;

        value = (long)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryReadNumberOrNil(int, out double?, out string)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryReadNumberOrNil(int parameterIndex, out long? value, [NotNullWhen(false)] out string? error)
    {
        value = null;
        if (!TryReadNumberOrNil(parameterIndex, out double? rawValue, out error))
            return false;

        value = (long?)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryReadNumber(int, out double, out string)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryReadNumber(int parameterIndex, out ulong value, [NotNullWhen(false)] out string? error)
    {
        value = 0;
        if (!TryReadNumber(parameterIndex, out double rawValue, out error))
            return false;

        value = (ulong)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryReadNumberOrNil(int, out double?, out string)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryReadNumberOrNil(int parameterIndex, out ulong? value, [NotNullWhen(false)] out string? error)
    {
        value = null;
        if (!TryReadNumberOrNil(parameterIndex, out double? rawValue, out error))
            return false;

        value = (ulong?)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryReadNumber(int, out double, out string)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/>.</remarks>
    public bool TryReadNumber(int parameterIndex, out float value, [NotNullWhen(false)] out string? error)
    {
        value = 0;
        if (!TryReadNumber(parameterIndex, out double rawValue, out error))
            return false;

        value = (float)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryReadNumberOrNil(int, out double?, out string)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/>.</remarks>
    public bool TryReadNumberOrNil(int parameterIndex, out float? value, [NotNullWhen(false)] out string? error)
    {
        value = null;
        if (!TryReadNumberOrNil(parameterIndex, out double? rawValue, out error))
            return false;

        value = (float?)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryReadNumber(int, out double, out string)"/>
    public bool TryReadNumber(int parameterIndex, out decimal value, [NotNullWhen(false)] out string? error)
    {
        value = 0;
        if (!TryReadNumber(parameterIndex, out double rawValue, out error))
            return false;

        value = (decimal)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryReadNumberOrNil(int, out double?, out string)"/>
    public bool TryReadNumberOrNil(int parameterIndex, out decimal? value, [NotNullWhen(false)] out string? error)
    {
        value = null;
        if (!TryReadNumberOrNil(parameterIndex, out double? rawValue, out error))
            return false;

        value = (decimal?)rawValue;
        return true;
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
