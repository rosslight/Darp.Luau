using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Darp.Luau;

/// <summary>
/// Input view used by <see cref="ILuauUserData{T}.OnSetIndex"/> callbacks.
/// Represents exactly one assigned value.
/// </summary>
public readonly ref struct LuauArgsSingle
{
    private readonly LuauArgs _args;

    /// <summary>
    /// Initializes a single-argument wrapper over <see cref="LuauArgs"/>.
    /// </summary>
    /// <param name="args">Underlying argument view.</param>
    internal LuauArgsSingle(LuauArgs args) => _args = args;

    /// <inheritdoc cref="LuauArgs.TryReadNumber(int, out double, out string)"/>
    public bool TryReadNumber(out double value, [NotNullWhen(false)] out string? error) =>
        _args.TryReadNumber(1, out value, out error);

    /// <inheritdoc cref="LuauArgs.TryReadNumberOrNil(int, out double?, out string)"/>
    public bool TryReadNumberOrNil(out double? value, [NotNullWhen(false)] out string? error) =>
        _args.TryReadNumberOrNil(1, out value, out error);

    /// <inheritdoc cref="LuauArgs.TryReadNumber{T}(int, out T, out string)"/>
    public bool TryReadNumber<T>(out T value, [NotNullWhen(false)] out string? error)
        where T : struct, INumber<T> => _args.TryReadNumber(1, out value, out error);

    /// <inheritdoc cref="LuauArgs.TryReadNumberOrNil{T}(int, out T?, out string)"/>
    public bool TryReadNumberOrNil<T>(out T? value, [NotNullWhen(false)] out string? error)
        where T : struct, INumber<T> => _args.TryReadNumberOrNil(1, out value, out error);

    /// <inheritdoc cref="LuauArgs.TryReadUtf8String(int, out ReadOnlySpan{byte}, out string)"/>
    public bool TryReadUtf8String(out ReadOnlySpan<byte> value, [NotNullWhen(false)] out string? error) =>
        _args.TryReadUtf8String(1, out value, out error);

    /// <inheritdoc cref="LuauArgs.TryReadUtf8String(int, out string, out string)"/>
    public bool TryReadUtf8String([NotNullWhen(true)] out string? value, [NotNullWhen(false)] out string? error) =>
        _args.TryReadUtf8String(1, out value, out error);

    /// <inheritdoc cref="LuauArgs.TryReadUtf8StringOrNil(int, out ReadOnlySpan{byte}, out bool, out string)"/>
    public bool TryReadUtf8StringOrNil(
        out ReadOnlySpan<byte> value,
        out bool isNil,
        [NotNullWhen(false)] out string? error
    ) => _args.TryReadUtf8StringOrNil(1, out value, out isNil, out error);

    /// <inheritdoc cref="LuauArgs.TryReadUtf8StringOrNil(int, out string, out string)"/>
    public bool TryReadUtf8StringOrNil(out string? value, [NotNullWhen(false)] out string? error) =>
        _args.TryReadUtf8StringOrNil(1, out value, out error);

    /// <inheritdoc cref="LuauArgs.TryReadBoolean(int, out bool, out string)"/>
    public bool TryReadBoolean(out bool value, [NotNullWhen(false)] out string? error) =>
        _args.TryReadBoolean(1, out value, out error);

    /// <inheritdoc cref="LuauArgs.TryReadBooleanOrNil(int, out bool?, out string)"/>
    public bool TryReadBooleanOrNil(out bool? value, [NotNullWhen(false)] out string? error) =>
        _args.TryReadBooleanOrNil(1, out value, out error);

    /// <inheritdoc cref="LuauArgs.TryReadBufferOrNil(int, out byte[], out string)"/>
    public bool TryReadBufferOrNil(out byte[]? value, [NotNullWhen(false)] out string? error) =>
        _args.TryReadBufferOrNil(1, out value, out error);

    /// <inheritdoc cref="LuauArgs.TryReadLuauValue(int, out LuauValue, out string)"/>
    public bool TryReadLuauValue(out LuauValue value, [NotNullWhen(false)] out string? error) =>
        _args.TryReadLuauValue(1, out value, out error);

    /// <inheritdoc cref="LuauArgs.TryReadLuauTable(int, out LuauTableView, out string)"/>
    public bool TryReadLuauTable(out LuauTableView value, [NotNullWhen(false)] out string? error) =>
        _args.TryReadLuauTable(1, out value, out error);

    /// <inheritdoc cref="LuauArgs.TryReadLuauFunction(int, out LuauFunctionView, out string)"/>
    public bool TryReadLuauFunction(out LuauFunctionView value, [NotNullWhen(false)] out string? error) =>
        _args.TryReadLuauFunction(1, out value, out error);

    /// <inheritdoc cref="LuauArgs.TryReadLuauString"/>
    public bool TryReadLuauString(out LuauStringView value, [NotNullWhen(false)] out string? error) =>
        _args.TryReadLuauString(1, out value, out error);

    /// <inheritdoc cref="LuauArgs.TryReadLuauBuffer(int, out LuauBufferView, out string)"/>
    public bool TryReadLuauBuffer(out LuauBufferView value, [NotNullWhen(false)] out string? error) =>
        _args.TryReadLuauBuffer(1, out value, out error);

    /// <inheritdoc cref="LuauArgs.TryReadLuauUserdata(int, out LuauUserdataView, out string)"/>
    public bool TryReadLuauUserdata(out LuauUserdataView value, [NotNullWhen(false)] out string? error) =>
        _args.TryReadLuauUserdata(1, out value, out error);

    /// <inheritdoc cref="LuauArgs.TryReadUserdata{T}(int, out T, out string)"/>
    public bool TryReadUserdata<T>([NotNullWhen(true)] out T? value, [NotNullWhen(false)] out string? error)
        where T : class, ILuauUserData<T> => _args.TryReadUserdata(1, out value, out error);

    /// <inheritdoc cref="LuauArgs.TryReadUserdataOrNil{T}(int, out T, out string)"/>
    public bool TryReadUserdataOrNil<T>(out T? value, [NotNullWhen(false)] out string? error)
        where T : class, ILuauUserData<T> => _args.TryReadUserdataOrNil(1, out value, out error);

    /// <inheritdoc cref="LuauArgs.TryReadBuffer(int, out ReadOnlySpan{byte}, out string)"/>
    public bool TryReadBuffer(out ReadOnlySpan<byte> value, [NotNullWhen(false)] out string? error) =>
        _args.TryReadBuffer(1, out value, out error);

    /// <inheritdoc cref="LuauArgs.TryReadBuffer(int, out byte[], out string)"/>
    public bool TryReadBuffer([NotNullWhen(true)] out byte[]? value, [NotNullWhen(false)] out string? error) =>
        _args.TryReadBuffer(1, out value, out error);

    /// <inheritdoc cref="LuauArgs.TryReadBufferOrNil(int, out ReadOnlySpan{byte}, out bool, out string)"/>
    public bool TryReadBufferOrNil(
        out ReadOnlySpan<byte> value,
        out bool isNil,
        [NotNullWhen(false)] out string? error
    ) => _args.TryReadBufferOrNil(1, out value, out isNil, out error);
}
