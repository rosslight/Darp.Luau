using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Darp.Luau.Native;
using Darp.Luau.Utils;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau;

/// <summary>
/// Identifies the kind of data represented by a <see cref="LuauValue"/>.
/// </summary>
public enum LuauValueType
{
    // Nil has to be 0 to allow default(LuauValue) to not cause problems
    /// <summary>Represents the Lua <c>nil</c> value.</summary>
    Nil = 0,

    /// <summary>Represents a Lua string.</summary>
    [SuppressMessage(
        "Naming",
        "CA1720:Identifier contains type name",
        Justification = "Matches the canonical Lua value kind and preserves public API compatibility."
    )]
    String,

    /// <summary>Represents a Lua number.</summary>
    Number,

    /// <summary>Represents a Lua boolean.</summary>
    Boolean,

    /// <summary>Represents a Lua table.</summary>
    Table,

    /// <summary>Represents a Lua function.</summary>
    Function,

    /// <summary>Represents a Luau coroutine. See <see cref="LuauCoroutine"/>.</summary>
    Thread,

    /// <summary>Represents a Lua userdata value.</summary>
    Userdata,

    /// <summary>Represents a Lua vector value.</summary>
    Vector,

    /// <summary>Represents a Lua buffer.</summary>
    Buffer,
}

/// <summary>
/// Represents a single Luau value, either inline or backed by a tracked registry reference.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[SuppressMessage(
    "Performance",
    "CA1815:Override equals and operator equals on value types",
    Justification = "This type can wrap Lua references; custom value equality would imply Lua identity semantics the API does not guarantee."
)]
public readonly struct LuauValue : IDisposable
{
    /// <summary>
    /// Gets the kind of value represented by this instance.
    /// </summary>
    public LuauValueType Type { get; }
    private readonly LuauState? _state;
    private readonly LuauValueUnion _union;

    [StructLayout(LayoutKind.Explicit)]
    private readonly struct LuauValueUnion
    {
        [FieldOffset(0)]
        public readonly bool ValueBool;

        [FieldOffset(0)]
        public readonly double ValueDouble;

        [FieldOffset(0)]
        public readonly ulong ValueHandle;

        public LuauValueUnion(bool value) => ValueBool = value;

        public LuauValueUnion(double value) => ValueDouble = value;

        public LuauValueUnion(ulong value) => ValueHandle = value;
    }

    private LuauValue(LuauState? state, LuauValueType type, LuauValueUnion union)
    {
        Type = type;
        _state = state;
        _union = union;
    }

    /// <summary>
    /// Creates a Luau boolean value from a managed boolean.
    /// </summary>
    /// <param name="value">The managed boolean value.</param>
    /// <returns>A <see cref="LuauValue"/> representing <paramref name="value"/>.</returns>
    public static explicit operator LuauValue(bool value) =>
        new(null, LuauValueType.Boolean, new LuauValueUnion(value));

    /// <summary>
    /// Creates a Luau number value from a managed double.
    /// </summary>
    /// <param name="value">The managed numeric value.</param>
    /// <returns>A <see cref="LuauValue"/> representing <paramref name="value"/>.</returns>
    public static explicit operator LuauValue(double value) =>
        new(null, LuauValueType.Number, new LuauValueUnion(value));

    internal static LuauValue Move(LuauState? state, in ulong handle, LuauValueType type)
    {
        ulong? newHandle = state?.ReferenceTracker.CountRefOrThrow(handle);
        state?.ReferenceTracker.ReleaseRef(handle);
        return new LuauValue(state, type, new LuauValueUnion(newHandle ?? 0));
    }

    /// <summary>
    /// Attempts to convert this Luau value to the requested managed type.
    /// </summary>
    /// <typeparam name="T">The managed target type.</typeparam>
    /// <param name="value">Receives the converted value when the conversion succeeds.</param>
    /// <param name="acceptNil">Allows <c>nil</c> to map to supported optional managed representations.</param>
    /// <returns><c>true</c> when conversion succeeds; otherwise <c>false</c>.</returns>
    public bool TryGet<T>([NotNullWhen(true)] out T? value, bool acceptNil = false)
        where T : allows ref struct
    {
        if (typeof(T) == typeof(LuauValue))
        {
            LuauValue temp = this;
            if (
                _state is not null
                && Type
                    is LuauValueType.String
                        or LuauValueType.Table
                        or LuauValueType.Function
                        or LuauValueType.Thread
                        or LuauValueType.Userdata
                        or LuauValueType.Buffer
            )
            {
                // A reference that was disposed, or whose state is gone, cannot be copied.
                if (_state.IsDisposed || !_state.ReferenceTracker.HasRegistryReference(_union.ValueHandle))
                {
                    value = default;
                    return false;
                }
                ulong newHandle = _state.ReferenceTracker.CountRefOrThrow(_union.ValueHandle);
                temp = new LuauValue(_state, Type, new LuauValueUnion(newHandle));
            }
            value = Unsafe.As<LuauValue, T>(ref temp)!;
            return true;
        }
        value = default;
        switch (Type)
        {
            case LuauValueType.Nil:
                if (typeof(T) == typeof(LuauNil))
                {
                    var temp = default(LuauNil);
                    value = Unsafe.As<LuauNil, T>(ref temp)!;
                    return true;
                }
                if (!acceptNil)
                {
                    return false;
                }
                if (typeof(T) == typeof(double?))
                {
                    double? temp = null;
                    value = Unsafe.As<double?, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(Half?))
                {
                    Half? temp = null;
                    value = Unsafe.As<Half?, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(float?))
                {
                    float? temp = null;
                    value = Unsafe.As<float?, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(decimal?))
                {
                    decimal? temp = null;
                    value = Unsafe.As<decimal?, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(double?))
                {
                    double? temp = null;
                    value = Unsafe.As<double?, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(sbyte?))
                {
                    sbyte? temp = null;
                    value = Unsafe.As<sbyte?, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(byte?))
                {
                    byte? temp = null;
                    value = Unsafe.As<byte?, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(short?))
                {
                    short? temp = null;
                    value = Unsafe.As<short?, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(ushort?))
                {
                    ushort? temp = null;
                    value = Unsafe.As<ushort?, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(int?))
                {
                    int? temp = null;
                    value = Unsafe.As<int?, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(uint?))
                {
                    uint? temp = null;
                    value = Unsafe.As<uint?, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(long?))
                {
                    long? temp = null;
                    value = Unsafe.As<long?, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(ulong?))
                {
                    ulong? temp = null;
                    value = Unsafe.As<ulong?, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(Int128?))
                {
                    Int128? temp = null;
                    value = Unsafe.As<Int128?, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(UInt128?))
                {
                    UInt128? temp = null;
                    value = Unsafe.As<UInt128?, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(bool?))
                {
                    bool? temp = null;
                    value = Unsafe.As<bool?, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(string))
                {
                    string? temp = null;
                    value = Unsafe.As<string?, T>(ref temp)!;
                    return true;
                }
                return false;
            case LuauValueType.String:
                if (_state is null || !_state.ReferenceTracker.HasRegistryReference(_union.ValueHandle))
                    return false;
                if (typeof(T) == typeof(string))
                {
                    ulong newHandle = _state.ReferenceTracker.CountRefOrThrow(_union.ValueHandle);
                    using var luauString = new LuauString(_state, newHandle);
                    string temp = luauString.ToString();
                    value = Unsafe.As<string, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(LuauString))
                {
                    ulong newHandle = _state.ReferenceTracker.CountRefOrThrow(_union.ValueHandle);
                    var temp = new LuauString(_state, newHandle);
                    value = Unsafe.As<LuauString, T>(ref temp)!;
                    return true;
                }
                return false;
            case LuauValueType.Number:
                if (typeof(T) == typeof(double))
                {
                    double temp = _union.ValueDouble;
                    value = Unsafe.As<double, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(Half))
                {
                    var temp = (Half)_union.ValueDouble;
                    value = Unsafe.As<Half, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(float))
                {
                    float temp = (float)_union.ValueDouble;
                    value = Unsafe.As<float, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(decimal))
                {
                    decimal temp = (decimal)_union.ValueDouble;
                    value = Unsafe.As<decimal, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(double))
                {
                    double temp = _union.ValueDouble;
                    value = Unsafe.As<double, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(sbyte))
                {
                    sbyte temp = (sbyte)_union.ValueDouble;
                    value = Unsafe.As<sbyte, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(byte))
                {
                    byte temp = (byte)_union.ValueDouble;
                    value = Unsafe.As<byte, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(short))
                {
                    short temp = (short)_union.ValueDouble;
                    value = Unsafe.As<short, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(ushort))
                {
                    ushort temp = (ushort)_union.ValueDouble;
                    value = Unsafe.As<ushort, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(int))
                {
                    int temp = (int)_union.ValueDouble;
                    value = Unsafe.As<int, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(uint))
                {
                    uint temp = (uint)_union.ValueDouble;
                    value = Unsafe.As<uint, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(long))
                {
                    long temp = (long)_union.ValueDouble;
                    value = Unsafe.As<long, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(ulong))
                {
                    ulong temp = (ulong)_union.ValueDouble;
                    value = Unsafe.As<ulong, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(Int128))
                {
                    var temp = (Int128)_union.ValueDouble;
                    value = Unsafe.As<Int128, T>(ref temp)!;
                    return true;
                }
                if (typeof(T) == typeof(UInt128))
                {
                    UInt128 temp = (UInt128)_union.ValueDouble;
                    value = Unsafe.As<UInt128, T>(ref temp)!;
                    return true;
                }
                return false;
            case LuauValueType.Boolean:
                if (typeof(T) == typeof(bool))
                {
                    bool temp = _union.ValueBool;
                    value = Unsafe.As<bool, T>(ref temp)!;
                    return true;
                }
                return false;
            case LuauValueType.Table:
                if (_state is null || !_state.ReferenceTracker.HasRegistryReference(_union.ValueHandle))
                    return false;
                if (typeof(T) == typeof(LuauTable))
                {
                    ulong newHandle = _state.ReferenceTracker.CountRefOrThrow(_union.ValueHandle);
                    var temp = new LuauTable(_state, newHandle);
                    value = Unsafe.As<LuauTable, T>(ref temp)!;
                    return true;
                }
                return false;
            case LuauValueType.Function:
                if (_state is null || !_state.ReferenceTracker.HasRegistryReference(_union.ValueHandle))
                    return false;
                if (typeof(T) == typeof(LuauFunction))
                {
                    ulong newHandle = _state.ReferenceTracker.CountRefOrThrow(_union.ValueHandle);
                    var temp = new LuauFunction(_state, newHandle);
                    value = Unsafe.As<LuauFunction, T>(ref temp)!;
                    return true;
                }
                return false;
            case LuauValueType.Thread:
                if (_state is null || !_state.ReferenceTracker.HasRegistryReference(_union.ValueHandle))
                    return false;
                if (typeof(T) == typeof(LuauCoroutine))
                {
                    ulong newHandle = _state.ReferenceTracker.CountRefOrThrow(_union.ValueHandle);
                    var temp = new LuauCoroutine(_state, newHandle);
                    value = Unsafe.As<LuauCoroutine, T>(ref temp);
                    return value is not null;
                }
                return false;
            case LuauValueType.Userdata:
                if (_state is null || !_state.ReferenceTracker.HasRegistryReference(_union.ValueHandle))
                    return false;
                if (typeof(T) == typeof(LuauUserdata))
                {
                    ulong newHandle = _state.ReferenceTracker.CountRefOrThrow(_union.ValueHandle);
                    var temp = new LuauUserdata(_state, newHandle);
                    value = Unsafe.As<LuauUserdata, T>(ref temp)!;
                    return true;
                }
                return false;
            case LuauValueType.Buffer:
                if (_state is null || !_state.ReferenceTracker.HasRegistryReference(_union.ValueHandle))
                    return false;
                if (typeof(T) == typeof(ReadOnlySpan<byte>))
                {
                    ulong newHandle = _state.ReferenceTracker.CountRefOrThrow(_union.ValueHandle);
                    using var temp = new LuauBuffer(_state, newHandle);
                    if (!temp.TryGet(out ReadOnlySpan<byte> span))
                        return false;

                    value = Unsafe.As<ReadOnlySpan<byte>, T>(ref span)!;
                    return true;
                }
                if (typeof(T) == typeof(byte[]))
                {
                    ulong newHandle = _state.ReferenceTracker.CountRefOrThrow(_union.ValueHandle);
                    using var temp = new LuauBuffer(_state, newHandle);
                    if (!temp.TryGet(out byte[]? bytes))
                        return false;

                    value = Unsafe.As<byte[], T>(ref bytes)!;
                    return true;
                }
                if (typeof(T) == typeof(LuauBuffer))
                {
                    ulong newHandle = _state.ReferenceTracker.CountRefOrThrow(_union.ValueHandle);
                    var temp = new LuauBuffer(_state, newHandle);
                    value = Unsafe.As<LuauBuffer, T>(ref temp)!;
                    return true;
                }
                return false;
            default:
                return false;
        }
    }

    /// <summary> Push a single value on the stack </summary>
    /// <param name="L"> The lua state </param>
    /// <exception cref="ArgumentOutOfRangeException"> Thrown if the Type is invalid </exception>
    internal unsafe void Push(lua_State* L)
    {
        if (_state is not null)
        {
            _state.ThrowIfDisposed();
            if (!_state.OwnsThread(L))
                throw new InvalidOperationException("Cross-state reference usage is not allowed.");
        }

        switch (Type)
        {
            case LuauValueType.Nil:
                lua_pushnil(L);
                break;
            case LuauValueType.Boolean:
                lua_pushboolean(L, _union.ValueBool ? 1 : 0);
                break;
            case LuauValueType.Number:
                lua_pushnumber(L, _union.ValueDouble);
                break;
            case LuauValueType.String
            or LuauValueType.Table
            or LuauValueType.Function
            or LuauValueType.Thread
            or LuauValueType.Userdata
            or LuauValueType.Buffer:
                if (_state is null)
                    throw new InvalidOperationException("No LuauState present.");
                var trackedReference = _state.GetTrackedReferenceOrThrow(_union.ValueHandle);
#pragma warning disable CA2000 // The pushed value is intentionally transferred to the caller's stack protocol and must remain on the stack.
                trackedReference.PushToTop();
#pragma warning restore CA2000
                if ((nint)_state.L != (nint)L)
                    lua_xmove(_state.L, L, 1);
                break;
            default:
                throw new InvalidOperationException($"Unsupported Luau value type: {Type}.");
        }
    }

    internal static unsafe LuauValue ToValue(LuauState state, int index = -1)
    {
        return ToValue(state, state.L, index);
    }

    internal static unsafe LuauValue ToValue(LuauState state, lua_State* L, int index = -1)
    {
        if (!state.OwnsThread(L))
            throw new InvalidOperationException("Cross-state value access is not allowed.");
#if DEBUG
        using var guard = new StackGuard(L, expectedDelta: 0);
#endif
        var type = (lua_Type)lua_type(L, index);
        switch (type)
        {
            case lua_Type.LUA_TNIL:
                return default;
            case lua_Type.LUA_TBOOLEAN:
                bool valueBool = lua_toboolean(L, index) == 1;
                return new LuauValue(state, LuauValueType.Boolean, new LuauValueUnion(valueBool));
            case lua_Type.LUA_TNUMBER:
                double valueNumber = lua_tonumber(L, index);
                return new LuauValue(state, LuauValueType.Number, new LuauValueUnion(valueNumber));
            case lua_Type.LUA_TSTRING:
                ulong referenceString = state.ReferenceTracker.TrackRef(L, index);
                return new LuauValue(state, LuauValueType.String, new LuauValueUnion(referenceString));
            case lua_Type.LUA_TTABLE:
                ulong referenceTable = state.ReferenceTracker.TrackRef(L, index);
                return new LuauValue(state, LuauValueType.Table, new LuauValueUnion(referenceTable));
            case lua_Type.LUA_TFUNCTION:
                ulong referenceFunction = state.ReferenceTracker.TrackRef(L, index);
                return new LuauValue(state, LuauValueType.Function, new LuauValueUnion(referenceFunction));
            case lua_Type.LUA_TTHREAD:
                ulong referenceThread = state.ReferenceTracker.TrackRef(L, index);
                return new LuauValue(state, LuauValueType.Thread, new LuauValueUnion(referenceThread));
            case lua_Type.LUA_TUSERDATA:
                ulong referenceUserdata = state.ReferenceTracker.TrackRef(L, index);
                return new LuauValue(state, LuauValueType.Userdata, new LuauValueUnion(referenceUserdata));
            case lua_Type.LUA_TBUFFER:
                ulong referenceBuffer = state.ReferenceTracker.TrackRef(L, index);
                return new LuauValue(state, LuauValueType.Buffer, new LuauValueUnion(referenceBuffer));
            default:
                throw new NotSupportedException($"The lua type {type} is not supported!");
        }
    }

    /// <summary>
    /// Releases an owned registry reference when this value represents a reference-backed Lua type.
    /// </summary>
    public void Dispose()
    {
        if (_state is null)
            return;
        if (
            Type
            is not LuauValueType.String
                and not LuauValueType.Table
                and not LuauValueType.Function
                and not LuauValueType.Thread
                and not LuauValueType.Userdata
                and not LuauValueType.Buffer
        )
            return;

        _state.ReferenceTracker.ReleaseRef(_union.ValueHandle);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        switch (Type)
        {
            case LuauValueType.Nil:
                return "nil";
            case LuauValueType.String:
            {
                _state.ThrowIfDisposed();
                ulong newHandle = _state.ReferenceTracker.CountRefOrThrow(_union.ValueHandle);
                using var temp = new LuauString(_state, newHandle);
                return temp.ToString();
            }
            case LuauValueType.Number:
                return _union.ValueDouble.ToString(CultureInfo.InvariantCulture);
            case LuauValueType.Boolean:
                return _union.ValueBool ? "true" : "false";
            case LuauValueType.Table:
                return new LuauTable(_state, _union.ValueHandle).ToString();
            case LuauValueType.Function:
                return new LuauFunction(_state, _union.ValueHandle).ToString();
            case LuauValueType.Thread:
                return new LuauCoroutine(_state, _union.ValueHandle).ToString();
            case LuauValueType.Userdata:
            {
                _state.ThrowIfDisposed();
                ulong newHandle = _state.ReferenceTracker.CountRefOrThrow(_union.ValueHandle);
                using var temp = new LuauUserdata(_state, newHandle);
                return temp.ToString();
            }
            case LuauValueType.Buffer:
            {
                _state.ThrowIfDisposed();
                ulong newHandle = _state.ReferenceTracker.CountRefOrThrow(_union.ValueHandle);
                using var temp = new LuauBuffer(_state, newHandle);
                return temp.ToString();
            }
            default:
                return "n/a";
        }
    }
}
