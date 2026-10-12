using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Darp.Luau.Native;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Internal;

/// <summary> A Luau value to read: a stack slot, or a value that is on no stack. </summary>
/// <remarks>
/// On no stack are a number, a boolean and nil, and a tracked reference of which only the type and the handle are
/// needed.
/// </remarks>
internal readonly unsafe ref struct ReadSource
{
    private readonly double _number;

    private ReadSource(
        LuauState? state,
        lua_State* L,
        int index,
        lua_Type type,
        ulong trackedHandle,
        double number,
        bool isMissing = false
    )
    {
        State = state;
        this.L = L;
        Index = index;
        Type = type;
        TrackedHandle = trackedHandle;
        _number = number;
        IsMissing = isMissing;
    }

    /// <summary> The state the value belongs to. A number, a boolean or nil may have none. </summary>
    public LuauState? State { get; }

    /// <summary> The stack the value is on, or null for a value that is on no stack. </summary>
    public lua_State* L { get; }

    public int Index { get; }

    public lua_Type Type { get; }

    /// <summary> The handle that already tracks the value, or 0. </summary>
    public ulong TrackedHandle { get; }

    public bool IsNil => Type is lua_Type.LUA_TNIL;

    /// <summary> Whether the value was not passed at all, like an omitted argument. It is nil. </summary>
    public bool IsMissing { get; }

    public double Number => L is null ? _number : lua_tonumber(L, Index);

    public bool Boolean => L is null ? _number != 0 : lua_toboolean(L, Index) != 0;

    public static ReadSource Nil => new(null, null, 0, lua_Type.LUA_TNIL, 0, 0);

    /// <summary> A value that was not passed. It is never looked up on a stack: a temporary may be where it would be. </summary>
    public static ReadSource Missing => new(null, null, 0, lua_Type.LUA_TNIL, 0, 0, isMissing: true);

    public static ReadSource FromStack(LuauState state, lua_State* L, int index) =>
        new(state, L, index, (lua_Type)lua_type(L, index), 0, 0);

    /// <param name="state">The state the value belongs to.</param>
    /// <param name="L">The stack the value is on.</param>
    /// <param name="index">The index of the value on that stack.</param>
    /// <param name="type">The type of the value, which the caller knows already.</param>
    /// <param name="trackedHandle">The handle that already tracks the value, or 0.</param>
    public static ReadSource FromStack(
        LuauState state,
        lua_State* L,
        int index,
        lua_Type type,
        ulong trackedHandle = 0
    ) => new(state, L, index, type, trackedHandle, 0);

    /// <summary> A tracked reference that is not pushed. It can be read as an owned reference, and as nothing else. </summary>
    public static ReadSource FromTracked(LuauState state, lua_Type type, ulong trackedHandle) =>
        new(state, null, 0, type, trackedHandle, 0);

    public static ReadSource FromNumber(LuauState? state, double number) =>
        new(state, null, 0, lua_Type.LUA_TNUMBER, 0, number);

    public static ReadSource FromBoolean(LuauState? state, bool boolean) =>
        new(state, null, 0, lua_Type.LUA_TBOOLEAN, 0, boolean ? 1 : 0);
}

/// <summary> Why a value could not be read. </summary>
/// <remarks>
/// A read that only asks whether a value has a type, like <c>TryGet</c>, fails often and never looks at the reason.
/// The text is therefore built when it is asked for, and a failure allocates nothing.
/// </remarks>
internal readonly struct ReadFailure
{
    private enum Kind
    {
        Mismatch,
        NumberDoesNotFit,
        NotSupported,
    }

    private readonly Kind _kind;

    // What was expected. For a read that is not supported: the whole message.
    private readonly string _expected;

    // The type that was found. Null for a value that was not passed.
    private readonly string? _actual;
    private readonly double _number;

    private ReadFailure(Kind kind, string expected, string? actual = null, double number = 0)
    {
        _kind = kind;
        _expected = expected;
        _actual = actual;
        _number = number;
    }

    /// <summary> The value has another type than was asked for. </summary>
    /// <param name="expected">The name of the Luau type that was asked for.</param>
    /// <param name="actual">The name of the type of the value, or null for a value that was not passed.</param>
    public static ReadFailure Mismatch(string expected, string? actual) => new(Kind.Mismatch, expected, actual);

    /// <summary> The value is a number that <paramref name="numberType"/> cannot hold. </summary>
    public static ReadFailure NumberDoesNotFit(string numberType, double number) =>
        new(Kind.NumberDoesNotFit, numberType, number: number);

    /// <summary> The requested type, or the value, has no managed form at all. </summary>
    public static ReadFailure NotSupported(string message) => new(Kind.NotSupported, message);

    /// <summary> Whether the read is not supported at all. That is an error of the host, not of the value. </summary>
    public bool IsNotSupported => _kind is Kind.NotSupported;

    /// <summary> What was expected and what was found, such as <c>number expected, got string</c>. </summary>
    public string Reason =>
        _kind switch
        {
            Kind.Mismatch when _actual is null => $"{_expected} expected",
            Kind.Mismatch => $"{_expected} expected, got {_actual}",
            Kind.NumberDoesNotFit =>
                $"number that fits {_expected} expected, got {_number.ToString(CultureInfo.InvariantCulture)}",
            _ => _expected,
        };

    /// <summary> The exception a throwing read raises. </summary>
    /// <param name="what">What was read, such as <c>result #1</c>.</param>
    public Exception ToException(string what) =>
        IsNotSupported ? new NotSupportedException(Reason) : new LuaGetException($"{what}: {Reason}");
}

/// <summary> The one place that turns a Luau value into a managed value. </summary>
/// <remarks>
/// <para>
/// Every typed read of the library ends here. Which managed type reads which Luau value is decided in
/// <see cref="TryRead{T}"/> and nowhere else.
/// </para>
/// <para>
/// A read does not throw for what it finds. Its caller has values on a stack and restores it before it acts on the
/// outcome.
/// </para>
/// </remarks>
internal static unsafe class LuauReader
{
    /// <summary> Reads <paramref name="source"/> as <typeparamref name="T"/>. </summary>
    /// <remarks>
    /// Nil is only read by a <typeparamref name="T"/> that has a value for it: a nullable value type, or
    /// <see cref="LuauValue"/>.
    /// </remarks>
    public static bool TryRead<T>(
        scoped in ReadSource source,
        [MaybeNullWhen(false)] out T value,
        out ReadFailure failure
    )
        where T : allows ref struct
    {
        // The comparisons of a value type T are decided when the method is compiled for it: one branch remains.
        if (typeof(T).IsValueType)
        {
            if (typeof(T) == typeof(double) || typeof(T) == typeof(double?))
                return Number<double, T>(source, out value, out failure);
            if (typeof(T) == typeof(int) || typeof(T) == typeof(int?))
                return Number<int, T>(source, out value, out failure);
            if (typeof(T) == typeof(float) || typeof(T) == typeof(float?))
                return Number<float, T>(source, out value, out failure);
            if (typeof(T) == typeof(long) || typeof(T) == typeof(long?))
                return Number<long, T>(source, out value, out failure);
            if (typeof(T) == typeof(uint) || typeof(T) == typeof(uint?))
                return Number<uint, T>(source, out value, out failure);
            if (typeof(T) == typeof(ulong) || typeof(T) == typeof(ulong?))
                return Number<ulong, T>(source, out value, out failure);
            if (typeof(T) == typeof(short) || typeof(T) == typeof(short?))
                return Number<short, T>(source, out value, out failure);
            if (typeof(T) == typeof(ushort) || typeof(T) == typeof(ushort?))
                return Number<ushort, T>(source, out value, out failure);
            if (typeof(T) == typeof(byte) || typeof(T) == typeof(byte?))
                return Number<byte, T>(source, out value, out failure);
            if (typeof(T) == typeof(sbyte) || typeof(T) == typeof(sbyte?))
                return Number<sbyte, T>(source, out value, out failure);
            if (typeof(T) == typeof(nint) || typeof(T) == typeof(nint?))
                return Number<nint, T>(source, out value, out failure);
            if (typeof(T) == typeof(nuint) || typeof(T) == typeof(nuint?))
                return Number<nuint, T>(source, out value, out failure);
            if (typeof(T) == typeof(decimal) || typeof(T) == typeof(decimal?))
                return Number<decimal, T>(source, out value, out failure);
            if (typeof(T) == typeof(Half) || typeof(T) == typeof(Half?))
                return Number<Half, T>(source, out value, out failure);
            if (typeof(T) == typeof(Int128) || typeof(T) == typeof(Int128?))
                return Number<Int128, T>(source, out value, out failure);
            if (typeof(T) == typeof(UInt128) || typeof(T) == typeof(UInt128?))
                return Number<UInt128, T>(source, out value, out failure);

            if (typeof(T) == typeof(bool) || typeof(T) == typeof(bool?))
            {
                return source.Type is lua_Type.LUA_TBOOLEAN
                    ? OkValue(source.Boolean, out value, out failure)
                    : NilOrMismatch<bool, T>(source, "boolean", out value, out failure);
            }

            if (typeof(T) == typeof(LuauValue))
            {
                return LuauValue.TryRead(source, out LuauValue read, out failure)
                    ? Ok(read, out value, out failure)
                    : Fail(out value);
            }

            if (typeof(T) == typeof(LuauTable) || typeof(T) == typeof(LuauTable?))
            {
                return source.Type is lua_Type.LUA_TTABLE
                    ? OkValue(new LuauTable(source.State, Own(source)), out value, out failure)
                    : NilOrMismatch<LuauTable, T>(source, "table", out value, out failure);
            }
            if (typeof(T) == typeof(LuauFunction) || typeof(T) == typeof(LuauFunction?))
            {
                return source.Type is lua_Type.LUA_TFUNCTION
                    ? OkValue(new LuauFunction(source.State, Own(source)), out value, out failure)
                    : NilOrMismatch<LuauFunction, T>(source, "function", out value, out failure);
            }
            if (typeof(T) == typeof(LuauString) || typeof(T) == typeof(LuauString?))
            {
                return source.Type is lua_Type.LUA_TSTRING
                    ? OkValue(new LuauString(source.State, Own(source)), out value, out failure)
                    : NilOrMismatch<LuauString, T>(source, "string", out value, out failure);
            }
            if (typeof(T) == typeof(LuauBuffer) || typeof(T) == typeof(LuauBuffer?))
            {
                return source.Type is lua_Type.LUA_TBUFFER
                    ? OkValue(new LuauBuffer(source.State, Own(source)), out value, out failure)
                    : NilOrMismatch<LuauBuffer, T>(source, "buffer", out value, out failure);
            }
            if (typeof(T) == typeof(LuauUserdata) || typeof(T) == typeof(LuauUserdata?))
            {
                return source.Type is lua_Type.LUA_TUSERDATA
                    ? OkValue(new LuauUserdata(source.State, Own(source)), out value, out failure)
                    : NilOrMismatch<LuauUserdata, T>(source, "userdata", out value, out failure);
            }
            if (typeof(T) == typeof(LuauCoroutine) || typeof(T) == typeof(LuauCoroutine?))
            {
                return source.Type is lua_Type.LUA_TTHREAD
                    ? OkValue(new LuauCoroutine(source.State, Own(source)), out value, out failure)
                    : NilOrMismatch<LuauCoroutine, T>(source, "thread", out value, out failure);
            }

            // A borrowed view or span is of the stack slot itself. Only a caller whose slot outlives the read asks for one.
            if (typeof(T) == typeof(LuauTableView))
            {
                return source.Type is lua_Type.LUA_TTABLE
                    ? Ok(new LuauTableView(State(source), source.L, source.Index), out value, out failure)
                    : Mismatch(source, "table", out value, out failure);
            }
            if (typeof(T) == typeof(LuauFunctionView))
            {
                return source.Type is lua_Type.LUA_TFUNCTION
                    ? Ok(new LuauFunctionView(State(source), source.L, source.Index), out value, out failure)
                    : Mismatch(source, "function", out value, out failure);
            }
            if (typeof(T) == typeof(LuauStringView))
            {
                return source.Type is lua_Type.LUA_TSTRING
                    ? Ok(new LuauStringView(State(source), source.L, source.Index), out value, out failure)
                    : Mismatch(source, "string", out value, out failure);
            }
            if (typeof(T) == typeof(LuauBufferView))
            {
                return source.Type is lua_Type.LUA_TBUFFER
                    ? Ok(new LuauBufferView(State(source), source.L, source.Index), out value, out failure)
                    : Mismatch(source, "buffer", out value, out failure);
            }
            if (typeof(T) == typeof(LuauUserdataView))
            {
                return source.Type is lua_Type.LUA_TUSERDATA
                    ? Ok(new LuauUserdataView(State(source), source.L, source.Index), out value, out failure)
                    : Mismatch(source, "userdata", out value, out failure);
            }
            if (typeof(T) == typeof(LuauCoroutineView))
            {
                return source.Type is lua_Type.LUA_TTHREAD
                    ? Ok(new LuauCoroutineView(State(source), source.L, source.Index), out value, out failure)
                    : Mismatch(source, "thread", out value, out failure);
            }
            if (typeof(T) == typeof(ReadOnlySpan<byte>))
            {
                return source.Type is lua_Type.LUA_TSTRING
                    ? Ok(StringBytes(source.L, source.Index), out value, out failure)
                    : Mismatch(source, "string", out value, out failure);
            }

            // An enum, or a nullable enum, reads the number its underlying type holds.
            return EnumInfo<T>.UnderlyingTypeCode switch
            {
                TypeCode.Int32 => Number<int, T>(source, out value, out failure),
                TypeCode.UInt32 => Number<uint, T>(source, out value, out failure),
                TypeCode.Int64 => Number<long, T>(source, out value, out failure),
                TypeCode.UInt64 => Number<ulong, T>(source, out value, out failure),
                TypeCode.Int16 => Number<short, T>(source, out value, out failure),
                TypeCode.UInt16 => Number<ushort, T>(source, out value, out failure),
                TypeCode.Byte => Number<byte, T>(source, out value, out failure),
                TypeCode.SByte => Number<sbyte, T>(source, out value, out failure),
                _ => NotSupported(out value, out failure),
            };
        }

        if (typeof(T) == typeof(string))
        {
            return source.Type is lua_Type.LUA_TSTRING
                ? Ok(Encoding.UTF8.GetString(StringBytes(source.L, source.Index)), out value, out failure)
                : Mismatch(source, "string", out value, out failure);
        }

        if (typeof(T) == typeof(byte[]))
        {
            return source.Type is lua_Type.LUA_TBUFFER
                ? Ok(BufferBytes(source.L, source.Index).ToArray(), out value, out failure)
                : Mismatch(source, "buffer", out value, out failure);
        }

        return ManagedUserdata(source, out value, out failure);
    }

    /// <summary> Disposes <paramref name="value"/> when it is a reference that a read created. </summary>
    /// <remarks>
    /// Every disposable value type a read returns is an owned reference or a <see cref="LuauValue"/>. A managed
    /// userdata is a class and belongs to the host, also when it is disposable.
    /// </remarks>
    public static void Release<T>(T value)
    {
        if (typeof(T).IsValueType && value is IDisposable disposable)
            disposable.Dispose();
    }

    /// <summary> The UTF-8 bytes of the string at <paramref name="index"/>, which Luau owns. </summary>
    public static ReadOnlySpan<byte> StringBytes(lua_State* L, int index)
    {
        nuint length = 0;
        byte* bytes = lua_tolstring(L, index, &length);
        return new ReadOnlySpan<byte>(bytes, checked((int)length));
    }

    /// <summary> The bytes of the buffer at <paramref name="index"/>, which Luau owns. </summary>
    public static ReadOnlySpan<byte> BufferBytes(lua_State* L, int index)
    {
        nuint length = 0;
        void* bytes = lua_tobuffer(L, index, &length);
        return new ReadOnlySpan<byte>(bytes, checked((int)length));
    }

    /// <summary> The name of the type of <paramref name="source"/> as scripts see it: what <c>typeof</c> returns in Luau. </summary>
    public static string TypeName(scoped in ReadSource source) =>
        source.Type switch
        {
            lua_Type.LUA_TNIL => "nil",
            lua_Type.LUA_TBOOLEAN => "boolean",
            lua_Type.LUA_TNUMBER => "number",
            lua_Type.LUA_TSTRING => "string",
            lua_Type.LUA_TTABLE => "table",
            lua_Type.LUA_TFUNCTION => "function",
            lua_Type.LUA_TTHREAD => "thread",
            lua_Type.LUA_TBUFFER => "buffer",
            lua_Type.LUA_TVECTOR => "vector",
            // A userdata type can have a name of its own, which only the value on a stack can tell.
            _ when source.L is not null => Encoding.UTF8.GetString(
                MemoryMarshal.CreateReadOnlySpanFromNullTerminated(luaL_typename(source.L, source.Index))
            ),
            lua_Type.LUA_TUSERDATA => "userdata",
            _ => throw new InvalidOperationException($"Unsupported Luau type: {source.Type}."),
        };

    /// <summary> Reads a number as <typeparamref name="TNumber"/>. </summary>
    /// <typeparam name="TNumber">The numeric type that holds the number.</typeparam>
    /// <typeparam name="T">
    /// <typeparamref name="TNumber"/>, an enum over it, or the nullable form of either. An enum has the layout of
    /// its underlying type, and a nullable enum that of the nullable underlying type.
    /// </typeparam>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Number<TNumber, T>(
        scoped in ReadSource source,
        [MaybeNullWhen(false)] out T value,
        out ReadFailure failure
    )
        where TNumber : struct, INumber<TNumber>
        where T : allows ref struct
    {
        if (source.Type is not lua_Type.LUA_TNUMBER)
            return NilOrMismatch<TNumber, T>(source, "number", out value, out failure);

        double number = source.Number;
        return LuauNumber.TryConvert(number, out TNumber converted)
            ? OkValue(converted, out value, out failure)
            : NumberMismatch<TNumber, T>(number, out value, out failure);
    }

    /// <summary> Gets a handle of its own for the value: an owned reference is created from it. </summary>
    /// <remarks> A value that is tracked already shares its registry reference instead of taking a second one. </remarks>
    internal static ulong Own(scoped in ReadSource source)
    {
        LuauState state = State(source);
        return source.TrackedHandle != 0
            ? state.ReferenceTracker.CountRefOrThrow(source.TrackedHandle)
            : state.ReferenceTracker.TrackRef(source.L, source.Index);
    }

    /// <summary> The state of a value that is a reference. Only a number, a boolean or nil can be without one. </summary>
    private static LuauState State(scoped in ReadSource source) =>
        source.State ?? throw new InvalidOperationException("No LuauState present.");

    private static bool ManagedUserdata<T>(
        scoped in ReadSource source,
        [MaybeNullWhen(false)] out T value,
        out ReadFailure failure
    )
        where T : allows ref struct
    {
        if (source.Type is lua_Type.LUA_TUSERDATA)
        {
            var native = (LuauUserdataNative*)lua_touserdatatagged(source.L, source.Index, LuauUserdataNative.Tag);
            if (native is not null && native->UserdataHandle is { IsAllocated: true, Target: T userdata })
            {
                value = userdata;
                failure = default;
                return true;
            }
        }

        return Mismatch(source, typeof(T).Name, out value, out failure);
    }

    /// <summary> Ends a read with <paramref name="read"/>, which is the <typeparamref name="T"/> that was asked for. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Ok<TValue, T>(TValue read, out T value, out ReadFailure failure)
        where TValue : allows ref struct
        where T : allows ref struct
    {
        Unsafe.SkipInit(out value);
        Unsafe.As<T, TValue>(ref value) = read;
        failure = default;
        return true;
    }

    /// <summary> Ends a read with a value of a value type. </summary>
    /// <typeparam name="TValue">The type of what was read.</typeparam>
    /// <typeparam name="T">
    /// <typeparamref name="TValue"/> or a type with its layout (an enum over it), or the nullable form of one of them.
    /// </typeparam>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool OkValue<TValue, T>(TValue read, out T value, out ReadFailure failure)
        where TValue : struct
        where T : allows ref struct
    {
        Unsafe.SkipInit(out value);
        if (IsNullable<TValue, T>())
            Unsafe.As<T, TValue?>(ref value) = read;
        else
            Unsafe.As<T, TValue>(ref value) = read;
        failure = default;
        return true;
    }

    /// <summary> Whether <typeparamref name="T"/> is the nullable form of <typeparamref name="TValue"/> and not the type itself. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsNullable<TValue, T>()
        where TValue : struct
        where T : allows ref struct => Unsafe.SizeOf<T>() != Unsafe.SizeOf<TValue>();

    /// <summary>
    /// Ends a read of a value type whose Luau type did not match: nil is the <c>null</c> of a nullable
    /// <typeparamref name="T"/>, everything else is a mismatch.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool NilOrMismatch<TValue, T>(
        scoped in ReadSource source,
        string expected,
        [MaybeNullWhen(false)] out T value,
        out ReadFailure failure
    )
        where TValue : struct
        where T : allows ref struct
    {
        if (!source.IsNil || !IsNullable<TValue, T>())
            return Mismatch(source, expected, out value, out failure);

        Unsafe.SkipInit(out value);
        Unsafe.As<T, TValue?>(ref value) = null;
        failure = default;
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Mismatch<T>(
        scoped in ReadSource source,
        string expected,
        [MaybeNullWhen(false)] out T value,
        out ReadFailure failure
    )
        where T : allows ref struct
    {
        value = default;
        // A value that was not passed has no type to name. The caller reports it as missing.
        failure = ReadFailure.Mismatch(expected, source.IsMissing ? null : TypeName(source));
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool NumberMismatch<TNumber, T>(
        double number,
        [MaybeNullWhen(false)] out T value,
        out ReadFailure failure
    )
        where T : allows ref struct
    {
        value = default;
        failure = ReadFailure.NumberDoesNotFit(typeof(TNumber).Name, number);
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool NotSupported<T>([MaybeNullWhen(false)] out T value, out ReadFailure failure)
        where T : allows ref struct
    {
        value = default;
        failure = ReadFailure.NotSupported($"A Luau value cannot be read as {typeof(T)}.");
        return false;
    }

    /// <summary> Ends a read whose failure is set already. </summary>
    private static bool Fail<T>([MaybeNullWhen(false)] out T value)
        where T : allows ref struct
    {
        value = default;
        return false;
    }

    private static class EnumInfo<T>
        where T : allows ref struct
    {
        /// <summary> The type code of the enum that <typeparamref name="T"/> is, or is the nullable form of. </summary>
        /// <remarks> <see cref="TypeCode.Empty"/> for every other type. </remarks>
        public static readonly TypeCode UnderlyingTypeCode = GetUnderlyingTypeCode();

        private static TypeCode GetUnderlyingTypeCode()
        {
            Type type = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            return type.IsEnum ? Type.GetTypeCode(type) : TypeCode.Empty;
        }
    }
}
