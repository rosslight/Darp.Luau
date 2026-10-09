using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text;
using Darp.Luau.Internal;

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

    /// <summary> Attempts to get the value for <paramref name="key"/> as a Lua number that <typeparamref name="T"/> can hold. </summary>
    /// <param name="key">Table key to resolve.</param>
    /// <param name="value">Resolved number when successful.</param>
    /// <typeparam name="T">The numeric type to read, such as <see cref="int"/> or <see cref="float"/>.</typeparam>
    /// <returns><c>true</c> when the value is a number that <typeparamref name="T"/> can hold; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// An integer type only takes whole numbers in its range: <c>1.5</c>, <c>-1</c> for an unsigned type, NaN and
    /// infinity fail instead of being truncated or wrapped. <see cref="float"/> and <see cref="Half"/> take the
    /// nearest value they have. <see cref="decimal"/> takes every finite number in its range.
    /// </remarks>
    public bool TryGetNumber<T>(IntoLuau key, out T value)
        where T : struct, INumber<T>
    {
        value = default;
        return TryGetNumber(key, out double number, out _) && LuauNumber.TryConvert(number, out value);
    }

    /// <summary>
    /// Attempts to get the value for <paramref name="key"/> as a Lua number that <typeparamref name="T"/> can hold,
    /// or as <c>nil</c>.
    /// </summary>
    /// <param name="key">Table key to resolve.</param>
    /// <param name="value">Resolved number, or <c>null</c> when the value is <c>nil</c>.</param>
    /// <typeparam name="T">The numeric type to read, such as <see cref="int"/> or <see cref="float"/>.</typeparam>
    /// <returns><c>true</c> when the value is <c>nil</c> or a number that <typeparamref name="T"/> can hold; otherwise <c>false</c>.</returns>
    /// <remarks> See <see cref="TryGetNumber{T}(IntoLuau, out T)"/> for what a type can hold. </remarks>
    public bool TryGetNumberOrNil<T>(IntoLuau key, out T? value)
        where T : struct, INumber<T>
    {
        value = null;
        if (!TryGetNumberOrNil(key, out double? number, out _))
            return false;
        if (number is not { } present)
            return true;
        if (!LuauNumber.TryConvert(present, out T converted))
            return false;

        value = converted;
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
        where T : class, ILuauUserdata<T> => TryGetUserdata(key, out value, out _);

    /// <summary>Attempts to get managed userdata of type <typeparamref name="T"/> or <c>nil</c> for <paramref name="key"/>.</summary>
    public bool TryGetUserdataOrNil<T>(IntoLuau key, out T? value)
        where T : class, ILuauUserdata<T> => TryGetUserdataOrNil(key, out value, out _);

    /// <summary>Gets managed userdata of type <typeparamref name="T"/> for <paramref name="key"/>.</summary>
    public T GetUserdata<T>(IntoLuau key)
        where T : class, ILuauUserdata<T> =>
        TryGetUserdata(key, out T? value, out string? error) ? value : throw CreateReadException(error);

    /// <summary>Gets managed userdata of type <typeparamref name="T"/> or <c>nil</c> for <paramref name="key"/>.</summary>
    public T? GetUserdataOrNil<T>(IntoLuau key)
        where T : class, ILuauUserdata<T> =>
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
