using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Darp.Luau.Internal;
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

    /// <summary> Gets this value as <typeparamref name="T"/>. </summary>
    /// <typeparam name="T">
    /// The managed type to read: a number type, an enum, <see cref="bool"/>, <see cref="string"/>, a byte array, a
    /// managed userdata type, an owned reference such as <see cref="LuauTable"/>, or <see cref="LuauValue"/> for a
    /// copy. The nullable form of a value type also reads nil. <see cref="LuauValue"/> reads nil itself and has no
    /// nullable form.
    /// </typeparam>
    /// <returns>The value. An owned reference is a reference of its own that the caller disposes.</returns>
    /// <exception cref="LuaGetException">Thrown when the value is not a <typeparamref name="T"/>.</exception>
    /// <exception cref="NotSupportedException">Thrown when <typeparamref name="T"/> is not a type a Luau value can be read as.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this value was disposed.</exception>
    public T Get<T>() =>
        Read<T>(isProbe: false, out var value, out ReadFailure failure) ? value : throw failure.ToException("value");

    /// <summary> Gets this value as <typeparamref name="T"/>, or <c>null</c> when it is nil. </summary>
    /// <typeparam name="T">A reference type <see cref="Get{T}"/> reads. For a value type, use its nullable form with <see cref="Get{T}"/>.</typeparam>
    /// <returns>The value, or <c>null</c>.</returns>
    /// <exception cref="LuaGetException">Thrown when the value is neither nil nor a <typeparamref name="T"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this value was disposed.</exception>
    public T? GetOrNull<T>()
        where T : class => Type is LuauValueType.Nil ? null : Get<T>();

    /// <summary> Attempts to get this value as <typeparamref name="T"/>. </summary>
    /// <typeparam name="T">The managed type to read. See <see cref="Get{T}"/>.</typeparam>
    /// <param name="value">The value when it is a <typeparamref name="T"/>.</param>
    /// <returns><c>true</c> when the value is a <typeparamref name="T"/>; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// Nil is read by the types that have a value for it: a nullable value type, for which
    /// <c>TryGet(out int? number)</c> returns <c>true</c> with <c>null</c>, and <see cref="LuauValue"/>.
    /// </remarks>
    /// <exception cref="NotSupportedException">Thrown when <typeparamref name="T"/> is not a type a Luau value can be read as.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this value was disposed.</exception>
    public bool TryGet<T>([NotNullWhen(true)] out T? value)
    {
        if (Read<T>(isProbe: true, out var read, out ReadFailure failure))
        {
            // Not null, except for the null of a nullable value type, which is what the caller asked to read nil as.
            // Annotated as the framework annotates the out parameter of a generic Try method, so that
            // TryGet(out string? text) infers string and leaves text not null.
            value = read!;
            return true;
        }

        value = default;
        return failure.IsNotSupported ? throw failure.ToException("value") : false;
    }

    /// <summary> Attempts to get this value as <typeparamref name="T"/>, or as <c>null</c> when it is nil. </summary>
    /// <typeparam name="T">A reference type <see cref="Get{T}"/> reads.</typeparam>
    /// <param name="value">The value, or <c>null</c> when it is nil.</param>
    /// <returns><c>true</c> when the value is nil or a <typeparamref name="T"/>; otherwise <c>false</c>.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when this value was disposed.</exception>
    public bool TryGetOrNull<T>(out T? value)
        where T : class
    {
        if (Type is not LuauValueType.Nil)
            return TryGet(out value);

        value = null;
        return true;
    }

    /// <param name="isProbe">Whether the read only asks if the value is a <typeparamref name="T"/>. See <see cref="ReadSource.IsProbe"/>.</param>
    /// <param name="value">The value when it was read.</param>
    /// <param name="failure">Why the value was not read.</param>
    private unsafe bool Read<T>(bool isProbe, [MaybeNullWhen(false)] out T value, out ReadFailure failure)
    {
        switch (Type)
        {
            case LuauValueType.Nil:
                return Read(ReadSource.Nil, isProbe, out value, out failure);
            case LuauValueType.Boolean:
                return Read(ReadSource.FromBoolean(_state, _union.ValueBool), isProbe, out value, out failure);
            case LuauValueType.Number:
                return Read(ReadSource.FromNumber(_state, _union.ValueDouble), isProbe, out value, out failure);
            default:
                RegistryReferenceTracker.TrackedReference reference = _state.GetTrackedReferenceOrThrow(
                    _union.ValueHandle
                );
                // A value type reads a reference as an owned reference only, which shares the handle and needs
                // nothing on the stack.
                if (typeof(T).IsValueType)
                {
                    return Read(
                        ReadSource.FromTracked(_state, GetLuaType(Type), _union.ValueHandle),
                        isProbe,
                        out value,
                        out failure
                    );
                }

                using (reference.PushToTop())
                {
                    return Read(
                        ReadSource.FromStack(_state, _state.L, -1, GetLuaType(Type), _union.ValueHandle),
                        isProbe,
                        out value,
                        out failure
                    );
                }
        }
    }

    // Not inlined, so that the reader is compiled into one place for a type and not into every case above.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Read<T>(
        scoped in ReadSource source,
        bool isProbe,
        [MaybeNullWhen(false)] out T value,
        out ReadFailure failure
    ) => LuauReader.TryRead(isProbe ? source.AsProbe() : source, out value, out failure);

    internal static LuauValueType GetValueType(lua_Type type) =>
        type switch
        {
            lua_Type.LUA_TNIL => LuauValueType.Nil,
            lua_Type.LUA_TBOOLEAN => LuauValueType.Boolean,
            lua_Type.LUA_TNUMBER => LuauValueType.Number,
            lua_Type.LUA_TSTRING => LuauValueType.String,
            lua_Type.LUA_TTABLE => LuauValueType.Table,
            lua_Type.LUA_TFUNCTION => LuauValueType.Function,
            lua_Type.LUA_TTHREAD => LuauValueType.Thread,
            lua_Type.LUA_TUSERDATA => LuauValueType.Userdata,
            lua_Type.LUA_TVECTOR => LuauValueType.Vector,
            lua_Type.LUA_TBUFFER => LuauValueType.Buffer,
            _ => throw new NotSupportedException($"The lua type {type} is not supported!"),
        };

    private static lua_Type GetLuaType(LuauValueType type) =>
        type switch
        {
            LuauValueType.String => lua_Type.LUA_TSTRING,
            LuauValueType.Table => lua_Type.LUA_TTABLE,
            LuauValueType.Function => lua_Type.LUA_TFUNCTION,
            LuauValueType.Thread => lua_Type.LUA_TTHREAD,
            LuauValueType.Userdata => lua_Type.LUA_TUSERDATA,
            LuauValueType.Buffer => lua_Type.LUA_TBUFFER,
            _ => throw new InvalidOperationException($"Unsupported Luau value type: {type}."),
        };

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

    /// <summary> Gets the value at <paramref name="index"/>, with a reference of its own when it is reference-backed. </summary>
    /// <exception cref="NotSupportedException">Thrown for a value that has no managed form, such as a vector.</exception>
    internal static unsafe LuauValue ToValue(LuauState state, lua_State* L, int index = -1)
    {
        if (!state.OwnsThread(L))
            throw new InvalidOperationException("Cross-state value access is not allowed.");
        return TryRead(ReadSource.FromStack(state, L, index), out LuauValue value, out ReadFailure failure)
            ? value
            : throw failure.ToException("value");
    }

    /// <summary> Reads <paramref name="source"/> as a value, with a reference of its own when it is reference-backed. </summary>
    internal static bool TryRead(scoped in ReadSource source, out LuauValue value, out ReadFailure failure)
    {
        // Written by a read that fails only. See ReadFailure.
        Unsafe.SkipInit(out failure);
        if (source.IsNil)
        {
            value = default;
            return true;
        }

        switch (source.Type)
        {
            case lua_Type.LUA_TBOOLEAN:
                value = new LuauValue(source.State, LuauValueType.Boolean, new LuauValueUnion(source.Boolean));
                return true;
            case lua_Type.LUA_TNUMBER:
                value = new LuauValue(source.State, LuauValueType.Number, new LuauValueUnion(source.Number));
                return true;
            case lua_Type.LUA_TSTRING:
                value = Own(source, LuauValueType.String);
                return true;
            case lua_Type.LUA_TTABLE:
                value = Own(source, LuauValueType.Table);
                return true;
            case lua_Type.LUA_TFUNCTION:
                value = Own(source, LuauValueType.Function);
                return true;
            case lua_Type.LUA_TTHREAD:
                value = Own(source, LuauValueType.Thread);
                return true;
            case lua_Type.LUA_TUSERDATA:
                value = Own(source, LuauValueType.Userdata);
                return true;
            case lua_Type.LUA_TBUFFER:
                value = Own(source, LuauValueType.Buffer);
                return true;
            default:
                value = default;
                failure = ReadFailure.NotSupported($"The lua type {source.Type} is not supported!");
                return false;
        }
    }

    private static LuauValue Own(scoped in ReadSource source, LuauValueType type) =>
        new(source.State, type, new LuauValueUnion(LuauReader.Own(source)));

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
