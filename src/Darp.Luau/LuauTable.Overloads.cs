using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Darp.Luau;

public partial struct LuauTable
{
    /// <summary> Gets the value for <paramref name="key"/> as a Lua number. </summary>
    /// <param name="key">Table key to resolve.</param>
    /// <returns>The resolved number.</returns>
    /// <exception cref="LuaGetException">Thrown when the value cannot be read as number.</exception>
    public double GetNumber(IntoLuau key) =>
        TryGetNumber(key, out double value, out string? error) ? value : throw CreateReadException(error);

    /// <summary> Gets the value for <paramref name="key"/> as a Lua number or <c>nil</c>. </summary>
    /// <param name="key">Table key to resolve.</param>
    /// <returns>The resolved number, or <c>null</c>.</returns>
    /// <exception cref="LuaGetException"> Thrown when the value is neither number nor <c>nil</c>.</exception>
    public double? GetNumberOrNil(IntoLuau key) =>
        TryGetNumberOrNil(key, out double? value, out string? error) ? value : throw CreateReadException(error);

    /// <summary> Attempts to get the value for <paramref name="key"/> as a Lua number. </summary>
    /// <param name="key">Table key to resolve.</param>
    /// <param name="value">Resolved number when successful.</param>
    /// <returns><c>true</c> when the value exists and is a number; otherwise <c>false</c>.</returns>
    public bool TryGetNumber(IntoLuau key, out double value) => TryGetNumber(key, out value, out _);

    /// <summary>
    /// Attempts to get the value for <paramref name="key"/> as a Lua number or <c>nil</c>.
    /// </summary>
    /// <param name="key">Table key to resolve.</param>
    /// <param name="value">Resolved number, or <c>null</c> when the value is <c>nil</c>.</param>
    /// <returns><c>true</c> when the value is number or <c>nil</c>; otherwise <c>false</c>.</returns>
    public bool TryGetNumberOrNil(IntoLuau key, out double? value) => TryGetNumberOrNil(key, out value, out _);

    /// <inheritdoc cref="TryGetNumber(IntoLuau, out double)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryGetNumber(IntoLuau key, out sbyte value)
    {
        value = 0;
        if (!TryGetNumber(key, out double rawValue, out _))
            return false;
        value = (sbyte)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryGetNumberOrNil(IntoLuau, out double?)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryGetNumberOrNil(IntoLuau key, out sbyte? value)
    {
        value = null;
        if (!TryGetNumberOrNil(key, out double? rawValue, out _))
            return false;
        value = (sbyte?)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryGetNumber(IntoLuau, out double)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryGetNumber(IntoLuau key, out byte value)
    {
        value = 0;
        if (!TryGetNumber(key, out double rawValue, out _))
            return false;
        value = (byte)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryGetNumberOrNil(IntoLuau, out double?)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryGetNumberOrNil(IntoLuau key, out byte? value)
    {
        value = null;
        if (!TryGetNumberOrNil(key, out double? rawValue, out _))
            return false;
        value = (byte?)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryGetNumber(IntoLuau, out double)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryGetNumber(IntoLuau key, out short value)
    {
        value = 0;
        if (!TryGetNumber(key, out double rawValue, out _))
            return false;

        value = (short)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryGetNumberOrNil(IntoLuau, out double?)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryGetNumberOrNil(IntoLuau key, out short? value)
    {
        value = null;
        if (!TryGetNumberOrNil(key, out double? rawValue, out _))
            return false;

        value = (short?)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryGetNumber(IntoLuau, out double)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryGetNumber(IntoLuau key, out ushort value)
    {
        value = 0;
        if (!TryGetNumber(key, out double rawValue, out _))
            return false;

        value = (ushort)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryGetNumberOrNil(IntoLuau, out double?)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryGetNumberOrNil(IntoLuau key, out ushort? value)
    {
        value = null;
        if (!TryGetNumberOrNil(key, out double? rawValue, out _))
            return false;

        value = (ushort?)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryGetNumber(IntoLuau, out double)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryGetNumber(IntoLuau key, out int value)
    {
        value = 0;
        if (!TryGetNumber(key, out double rawValue, out _))
            return false;

        value = (int)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryGetNumberOrNil(IntoLuau, out double?)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryGetNumberOrNil(IntoLuau key, out int? value)
    {
        value = null;
        if (!TryGetNumberOrNil(key, out double? rawValue, out _))
            return false;

        value = (int?)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryGetNumber(IntoLuau, out double)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryGetNumber(IntoLuau key, out uint value)
    {
        value = 0;
        if (!TryGetNumber(key, out double rawValue, out _))
            return false;

        value = (uint)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryGetNumberOrNil(IntoLuau, out double?)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryGetNumberOrNil(IntoLuau key, out uint? value)
    {
        value = null;
        if (!TryGetNumberOrNil(key, out double? rawValue, out _))
            return false;

        value = (uint?)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryGetNumber(IntoLuau, out double)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryGetNumber(IntoLuau key, out long value)
    {
        value = 0;
        if (!TryGetNumber(key, out double rawValue, out _))
            return false;

        value = (long)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryGetNumberOrNil(IntoLuau, out double?)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryGetNumberOrNil(IntoLuau key, out long? value)
    {
        value = null;
        if (!TryGetNumberOrNil(key, out double? rawValue, out _))
            return false;

        value = (long?)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryGetNumber(IntoLuau, out double)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryGetNumber(IntoLuau key, out ulong value)
    {
        value = 0;
        if (!TryGetNumber(key, out double rawValue, out _))
            return false;

        value = (ulong)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryGetNumberOrNil(IntoLuau, out double?)"/>
    /// <remarks>Conversion uses a direct cast from Lua <see cref="double"/> and may truncate fractional values.</remarks>
    public bool TryGetNumberOrNil(IntoLuau key, out ulong? value)
    {
        value = null;
        if (!TryGetNumberOrNil(key, out double? rawValue, out _))
            return false;

        value = (ulong?)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryGetNumber(IntoLuau, out double)"/>
    public bool TryGetNumber(IntoLuau key, out float value)
    {
        value = 0;
        if (!TryGetNumber(key, out double rawValue, out _))
            return false;

        value = (float)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryGetNumberOrNil(IntoLuau, out double?)"/>
    public bool TryGetNumberOrNil(IntoLuau key, out float? value)
    {
        value = null;
        if (!TryGetNumberOrNil(key, out double? rawValue, out _))
            return false;

        value = (float?)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryGetNumber(IntoLuau, out double)"/>
    public bool TryGetNumber(IntoLuau key, out decimal value)
    {
        value = 0;
        if (!TryGetNumber(key, out double rawValue, out _))
            return false;

        value = (decimal)rawValue;
        return true;
    }

    /// <inheritdoc cref="TryGetNumberOrNil(IntoLuau, out double?)"/>
    public bool TryGetNumberOrNil(IntoLuau key, out decimal? value)
    {
        value = null;
        if (!TryGetNumberOrNil(key, out double? rawValue, out _))
            return false;

        value = (decimal?)rawValue;
        return true;
    }

    /// <summary> Gets the value for <paramref name="key"/> as a Lua boolean. </summary>
    /// <param name="key">Table key to resolve.</param>
    /// <returns>The resolved boolean.</returns>
    /// <exception cref="LuaGetException">Thrown when the value cannot be read as boolean.</exception>
    public bool GetBoolean(IntoLuau key) =>
        TryGetBoolean(key, out bool value, out string? error) ? value : throw CreateReadException(error);

    /// <summary> Gets the value for <paramref name="key"/> as a Lua boolean or <c>nil</c>. </summary>
    /// <param name="key">Table key to resolve.</param>
    /// <returns>The resolved boolean, or <c>null</c>.</returns>
    /// <exception cref="LuaGetException">Thrown when the value is neither boolean nor <c>nil</c>.</exception>
    public bool? GetBooleanOrNil(IntoLuau key) =>
        TryGetBooleanOrNil(key, out bool? value, out string? error) ? value : throw CreateReadException(error);

    /// <summary> Attempts to get the value for <paramref name="key"/> as a Lua boolean. </summary>
    /// <param name="key">Table key to resolve.</param>
    /// <param name="value">Resolved boolean when successful.</param>
    /// <returns><c>true</c> when the value exists and is a boolean; otherwise <c>false</c>.</returns>
    public bool TryGetBoolean(IntoLuau key, out bool value) => TryGetBoolean(key, out value, out _);

    /// <summary> Attempts to get the value for <paramref name="key"/> as a Lua boolean or <c>nil</c>. </summary>
    /// <param name="key">Table key to resolve.</param>
    /// <param name="value">Resolved boolean, or <c>null</c> when the value is <c>nil</c>.</param>
    /// <returns><c>true</c> when the value is boolean or <c>nil</c>; otherwise <c>false</c>.</returns>
    public bool TryGetBooleanOrNil(IntoLuau key, out bool? value) => TryGetBooleanOrNil(key, out value, out _);

    /// <summary> Gets the value for <paramref name="key"/> as a UTF-8 string decoded to managed text. </summary>
    /// <param name="key">Table key to resolve.</param>
    /// <returns>The resolved managed string.</returns>
    /// <exception cref="LuaGetException">Thrown when the value cannot be read as string.</exception>
    public string GetUtf8String(IntoLuau key) =>
        TryGetUtf8String(key, out ReadOnlySpan<byte> value, out string? error)
            ? Encoding.UTF8.GetString(value)
            : throw CreateReadException(error);

    /// <summary> Gets the value for <paramref name="key"/> as a UTF-8 string decoded to managed text or <c>nil</c>. </summary>
    /// <param name="key">Table key to resolve.</param>
    /// <returns>The resolved managed string, or <c>null</c>.</returns>
    /// <exception cref="LuaGetException">Thrown when the value is neither string nor <c>nil</c>.</exception>
    public string? GetUtf8StringOrNil(IntoLuau key) =>
        TryGetUtf8StringOrNil(key, out ReadOnlySpan<byte> value, out bool isNil, out string? error)
            ? isNil
                ? null
                : Encoding.UTF8.GetString(value)
            : throw CreateReadException(error);

    /// <summary> Attempts to get the value for <paramref name="key"/> as a UTF-8 string. </summary>
    /// <param name="key">Table key to resolve.</param>
    /// <param name="value">Resolved UTF-8 bytes.</param>
    /// <returns><c>true</c> when the value exists and is a string; otherwise <c>false</c>.</returns>
    public bool TryGetUtf8String(IntoLuau key, out ReadOnlySpan<byte> value) => TryGetUtf8String(key, out value, out _);

    /// <summary> Attempts to get the value for <paramref name="key"/> as a UTF-8 string or <c>nil</c>. </summary>
    /// <param name="key">Table key to resolve.</param>
    /// <param name="value">Resolved UTF-8 bytes, or <c>default</c> when the value is <c>nil</c>.</param>
    /// <param name="isNil">Set to <c>true</c> when the value is <c>nil</c>.</param>
    /// <returns><c>true</c> when the value is string or <c>nil</c>; otherwise <c>false</c>.</returns>
    public bool TryGetUtf8StringOrNil(IntoLuau key, out ReadOnlySpan<byte> value, out bool isNil) =>
        TryGetUtf8StringOrNil(key, out value, out isNil, out _);

    /// <summary>
    /// Attempts to get the value for <paramref name="key"/> as a UTF-8 string decoded to managed text.
    /// </summary>
    /// <param name="key">Table key to resolve.</param>
    /// <param name="value">Resolved managed string when successful.</param>
    /// <returns><c>true</c> when the value exists and is a string; otherwise <c>false</c>.</returns>
    public bool TryGetUtf8String(IntoLuau key, [NotNullWhen(true)] out string? value)
    {
        value = null;
        if (!TryGetUtf8String(key, out ReadOnlySpan<byte> bytes, out _))
            return false;
        value = Encoding.UTF8.GetString(bytes);
        return true;
    }

    /// <summary>
    /// Attempts to get the value for <paramref name="key"/> as a UTF-8 string decoded to managed text or <c>nil</c>.
    /// </summary>
    /// <param name="key">Table key to resolve.</param>
    /// <param name="value">Resolved managed string, or <c>null</c> when the value is <c>nil</c>.</param>
    /// <returns><c>true</c> when the value is string or <c>nil</c>; otherwise <c>false</c>.</returns>
    public bool TryGetUtf8StringOrNil(IntoLuau key, out string? value)
    {
        value = null;
        if (!TryGetUtf8StringOrNil(key, out ReadOnlySpan<byte> bytes, out bool isNil, out _))
            return false;
        if (isNil)
            return true;
        value = isNil ? null : Encoding.UTF8.GetString(bytes);
        return true;
    }

    /// <summary> Gets the value for <paramref name="key"/> as a managed byte array. </summary>
    /// <param name="key">Table key to resolve.</param>
    /// <returns>The resolved managed byte array.</returns>
    /// <exception cref="LuaGetException">Thrown when the value cannot be read as buffer.</exception>
    public byte[] GetBuffer(IntoLuau key) =>
        TryGetBuffer(key, out ReadOnlySpan<byte> value, out string? error)
            ? value.ToArray()
            : throw CreateReadException(error);

    /// <summary> Gets the value for <paramref name="key"/> as a managed byte array or <c>nil</c>. </summary>
    /// <param name="key">Table key to resolve.</param>
    /// <returns>The resolved managed byte array, or <c>null</c>.</returns>
    /// <exception cref="LuaGetException">Thrown when the value is neither buffer nor <c>nil</c>.</exception>
    public byte[]? GetBufferOrNil(IntoLuau key) =>
        TryGetBufferOrNil(key, out ReadOnlySpan<byte> value, out bool isNil, out string? error)
            ? isNil
                ? null
                : value.ToArray()
            : throw CreateReadException(error);

    /// <summary> Attempts to get the value for <paramref name="key"/> as a Lua buffer. </summary>
    /// <param name="key">Table key to resolve.</param>
    /// <param name="value">Resolved buffer bytes.</param>
    /// <returns><c>true</c> when the value exists and is a buffer; otherwise <c>false</c>.</returns>
    public bool TryGetBuffer(IntoLuau key, out ReadOnlySpan<byte> value) => TryGetBuffer(key, out value, out _);

    /// <summary> Attempts to get the value for <paramref name="key"/> as a Lua buffer or <c>nil</c>. </summary>
    /// <param name="key">Table key to resolve.</param>
    /// <param name="value">Resolved buffer bytes, or <c>default</c> when the value is <c>nil</c>.</param>
    /// <param name="isNil">Set to <c>true</c> when the value is <c>nil</c>.</param>
    /// <returns><c>true</c> when the value is buffer or <c>nil</c>; otherwise <c>false</c>.</returns>
    public bool TryGetBufferOrNil(IntoLuau key, out ReadOnlySpan<byte> value, out bool isNil) =>
        TryGetBufferOrNil(key, out value, out isNil, out _);

    /// <summary>
    /// Attempts to get the value for <paramref name="key"/> as a managed byte array.
    /// </summary>
    /// <param name="key">Table key to resolve.</param>
    /// <param name="value">Resolved managed byte array when successful.</param>
    /// <returns><c>true</c> when the value exists and is a buffer; otherwise <c>false</c>.</returns>
    public bool TryGetBuffer(IntoLuau key, [NotNullWhen(true)] out byte[]? value)
    {
        value = null;
        if (!TryGetBuffer(key, out ReadOnlySpan<byte> rawValue, out _))
            return false;
        value = rawValue.ToArray();
        return true;
    }

    /// <summary>
    /// Attempts to get the value for <paramref name="key"/> as a managed byte array or <c>nil</c>.
    /// </summary>
    /// <param name="key">Table key to resolve.</param>
    /// <param name="value">Resolved managed byte array, or <c>null</c> when the value is <c>nil</c>.</param>
    /// <returns><c>true</c> when the value is buffer or <c>nil</c>; otherwise <c>false</c>.</returns>
    public bool TryGetBufferOrNil(IntoLuau key, out byte[]? value)
    {
        value = null;
        if (!TryGetBufferOrNil(key, out ReadOnlySpan<byte> rawValue, out bool isNil, out _))
            return false;
        if (isNil)
            return true;
        value = rawValue.ToArray();
        return true;
    }

    /// <summary>Attempts to get managed userdata of type <typeparamref name="T"/> for <paramref name="key"/>.</summary>
    public bool TryGetUserdata<T>(IntoLuau key, [NotNullWhen(true)] out T? value)
        where T : class, ILuauUserData<T> => TryGetUserdata(key, out value, out _);

    /// <summary>Attempts to get managed userdata of type <typeparamref name="T"/> or <c>nil</c> for <paramref name="key"/>.</summary>
    public bool TryGetUserdataOrNil<T>(IntoLuau key, out T? value)
        where T : class, ILuauUserData<T> => TryGetUserdataOrNil(key, out value, out _);

    /// <summary>Gets managed userdata of type <typeparamref name="T"/> for <paramref name="key"/>.</summary>
    public T GetUserdata<T>(IntoLuau key)
        where T : class, ILuauUserData<T> =>
        TryGetUserdata(key, out T? value, out string? error) ? value : throw CreateReadException(error);

    /// <summary>Gets managed userdata of type <typeparamref name="T"/> or <c>nil</c> for <paramref name="key"/>.</summary>
    public T? GetUserdataOrNil<T>(IntoLuau key)
        where T : class, ILuauUserData<T> =>
        TryGetUserdataOrNil(key, out T? value, out string? error) ? value : throw CreateReadException(error);

    /// <summary> Gets a non-nil value for <paramref name="key"/> as <see cref="LuauValue"/>. </summary>
    /// <param name="key">Table key to resolve.</param>
    /// <returns>The resolved value.</returns>
    /// <exception cref="LuaGetException">Thrown when the value is <c>nil</c>.</exception>
    public LuauValue GetLuauValue(IntoLuau key) =>
        TryGetLuauValue(key, out LuauValue value, out string? error) ? value : throw CreateReadException(error);

    /// <summary> Attempts to get a non-nil value for <paramref name="key"/> as <see cref="LuauValue"/>. </summary>
    /// <param name="key">Table key to resolve.</param>
    /// <param name="value">Resolved value when successful.</param>
    /// <returns><c>true</c></returns>
    public bool TryGetLuauValue(IntoLuau key, out LuauValue value) => TryGetLuauValue(key, out value, out _);

    /// <summary>Gets the value for <paramref name="key"/> as <see cref="LuauTable"/>.</summary>
    public LuauTable GetLuauTable(IntoLuau key) =>
        TryGetLuauTable(key, out LuauTable value, out string? error) ? value : throw CreateReadException(error);

    /// <summary>Attempts to get the value for <paramref name="key"/> as <see cref="LuauTable"/>.</summary>
    public bool TryGetLuauTable(IntoLuau key, out LuauTable value) => TryGetLuauTable(key, out value, out _);

    /// <summary>Gets the value for <paramref name="key"/> as <see cref="LuauFunction"/>.</summary>
    public LuauFunction GetLuauFunction(IntoLuau key) =>
        TryGetLuauFunction(key, out LuauFunction value, out string? error) ? value : throw CreateReadException(error);

    /// <summary>Attempts to get the value for <paramref name="key"/> as <see cref="LuauFunction"/>.</summary>
    public bool TryGetLuauFunction(IntoLuau key, out LuauFunction value) => TryGetLuauFunction(key, out value, out _);

    /// <summary>Gets the value for <paramref name="key"/> as <see cref="LuauCoroutine"/>.</summary>
    public LuauCoroutine GetLuauCoroutine(IntoLuau key) =>
        TryGetLuauCoroutine(key, out LuauCoroutine value, out string? error) ? value : throw CreateReadException(error);

    /// <summary>Attempts to get the value for <paramref name="key"/> as <see cref="LuauCoroutine"/>.</summary>
    public bool TryGetLuauCoroutine(IntoLuau key, out LuauCoroutine value) =>
        TryGetLuauCoroutine(key, out value, out _);

    /// <summary>Gets the value for <paramref name="key"/> as <see cref="LuauString"/>.</summary>
    public LuauString GetLuauString(IntoLuau key) =>
        TryGetLuauString(key, out LuauString value, out string? error) ? value : throw CreateReadException(error);

    /// <summary>Attempts to get the value for <paramref name="key"/> as <see cref="LuauString"/>.</summary>
    public bool TryGetLuauString(IntoLuau key, out LuauString value) => TryGetLuauString(key, out value, out _);

    /// <summary>Gets the value for <paramref name="key"/> as <see cref="LuauBuffer"/>.</summary>
    public LuauBuffer GetLuauBuffer(IntoLuau key) =>
        TryGetLuauBuffer(key, out LuauBuffer value, out string? error) ? value : throw CreateReadException(error);

    /// <summary>Attempts to get the value for <paramref name="key"/> as <see cref="LuauBuffer"/>.</summary>
    public bool TryGetLuauBuffer(IntoLuau key, out LuauBuffer value) => TryGetLuauBuffer(key, out value, out _);

    /// <summary>Gets the value for <paramref name="key"/> as <see cref="LuauUserdata"/>.</summary>
    public LuauUserdata GetLuauUserdata(IntoLuau key) =>
        TryGetLuauUserdata(key, out LuauUserdata value, out string? error) ? value : throw CreateReadException(error);

    /// <summary>Attempts to get the value for <paramref name="key"/> as <see cref="LuauUserdata"/>.</summary>
    public bool TryGetLuauUserdata(IntoLuau key, out LuauUserdata value) => TryGetLuauUserdata(key, out value, out _);
}
