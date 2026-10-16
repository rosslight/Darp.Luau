using System.Diagnostics;
using System.Text;
using Darp.Luau.Internal;
using Darp.Luau.Native;
using Darp.Luau.Utils;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau;

/// <summary>
/// Represents a temporary value that knows how to push itself onto a Luau stack.
/// </summary>
/// <remarks>
/// Built-in conversions are provided for common .NET types. Custom types can participate via implicit conversions.
/// </remarks>
public readonly ref struct IntoLuau
{
    internal enum Kind
    {
        // ReSharper disable once UnusedMember.Local -> Important for detecting a default struct
        Nil = 0,
        Bool,
        Number,
        Integer,
        Unsigned,
        Chars,
        Value,
        Buffer,
        UserdataFactory,
        Userdata,
        Reference,
    }

    /// <summary> Describes of which kind the resulting <see cref="LuauValue"/> will be </summary>
    internal Kind Type { get; }
    private readonly bool _bool;
    private readonly double _number;
    private readonly int _integer;
    private readonly ReadOnlySpan<char> _readOnlySpanChar;
    private readonly ReadOnlySpan<byte> _readOnlyBuffer;
    private readonly LuauValue _luauValue;
    private readonly Func<LuauState, LuauUserdata>? _factory;
    private readonly object? _userdata;
    private readonly Action<LuauState, object>? _pushUserdata;
    private readonly LuauSlot _slot;

    private IntoLuau(bool valueBool) => (Type, _bool) = (Kind.Bool, valueBool);

    private IntoLuau(double valueNumber) => (Type, _number) = (Kind.Number, valueNumber);

    private IntoLuau(int valueInteger) => (Type, _integer) = (Kind.Integer, valueInteger);

    private IntoLuau(uint valueUnsigned) => (Type, _integer) = (Kind.Unsigned, (int)valueUnsigned);

    private IntoLuau(ReadOnlySpan<char> valueChars)
    {
        Type = Kind.Chars;
        _readOnlySpanChar = valueChars;
    }

    private IntoLuau(ReadOnlySpan<byte> valueChars)
    {
        Type = Kind.Buffer;
        _readOnlyBuffer = valueChars;
    }

    private IntoLuau(LuauValue value)
    {
        Type = Kind.Value;
        _luauValue = value;
    }

    private IntoLuau(Func<LuauState, LuauUserdata> factory)
    {
        Type = Kind.UserdataFactory;
        _factory = factory;
    }

    private IntoLuau(object userdata, Action<LuauState, object> pushUserdata)
    {
        Type = Kind.Userdata;
        _userdata = userdata;
        _pushUserdata = pushUserdata;
    }

    private IntoLuau(LuauSlot slot)
    {
        Type = Kind.Reference;
        _slot = slot;
    }

    /// <summary> Refers to the value in <paramref name="slot"/> without taking a reference of its own. </summary>
    internal static IntoLuau FromSlot(LuauSlot slot) => new(slot);

    /// <summary> Refers to the owned reference <paramref name="handle"/> without taking a reference of its own. </summary>
    /// <remarks> A reference that was never set, like that of <c>default</c>, is nil. </remarks>
    internal static IntoLuau Borrow(LuauState? state, ulong handle) =>
        state is null ? default : new IntoLuau(state.GetSlotOrThrow(handle));

    internal unsafe void Push(LuauState state)
    {
        Push(state, state.L);
    }

    internal unsafe void Push(LuauState state, lua_State* L)
    {
        if (!state.OwnsThread(L))
            throw new InvalidOperationException("Cross-state value push is not allowed.");

        switch (Type)
        {
            case Kind.Chars:
                if (_readOnlySpanChar.Length > 256)
                {
                    Span<byte> buffer = new byte[Encoding.UTF8.GetByteCount(_readOnlySpanChar)];
                    int numberOfBytes = Encoding.UTF8.GetBytes(_readOnlySpanChar, buffer);
                    LuauStateMarshal.PushString(L, buffer[..numberOfBytes]);
                }
                else
                {
                    Span<byte> buffer = stackalloc byte[Encoding.UTF8.GetByteCount(_readOnlySpanChar)];
                    int numberOfBytes = Encoding.UTF8.GetBytes(_readOnlySpanChar, buffer);
                    LuauStateMarshal.PushString(L, buffer[..numberOfBytes]);
                }
                break;
            case Kind.Buffer:
                void* pDest = lua_newbuffer(L, (nuint)_readOnlyBuffer.Length);
                var destination = new Span<byte>(pDest, _readOnlyBuffer.Length);
                _readOnlyBuffer.CopyTo(destination);
                break;
            case Kind.Bool:
                lua_pushboolean(L, _bool ? 1 : 0);
                break;
            case Kind.Number:
                lua_pushnumber(L, _number);
                break;
            case Kind.Integer:
                lua_pushinteger(L, _integer);
                break;
            case Kind.Unsigned:
                lua_pushunsigned(L, (uint)_integer);
                break;
            case Kind.Value:
                _luauValue.Push(L);
                break;
            case Kind.UserdataFactory:
                Debug.Assert(_factory is not null);
                LuauUserdata userdata = _factory.Invoke(state);

                if (userdata.Equals(default))
                {
                    lua_pushnil(L);
                    break;
                }

                try
                {
                    IntoLuau l = userdata;
                    l.Push(state, L);
                }
                finally
                {
                    userdata.Dispose();
                }
                break;
            case Kind.Userdata:
                Debug.Assert(_userdata is not null && _pushUserdata is not null);
                // Straight onto the stack: a reference in the registry would only be released again
                _pushUserdata(state, _userdata);
                if ((nint)state.L != (nint)L)
                    lua_xmove(state.L, L, 1);
                break;
            case Kind.Reference:
                if (!ReferenceEquals(state, _slot.State))
                    throw new InvalidOperationException("Cross-state reference usage is not allowed.");
                _slot.Push(state);
                if ((nint)state.L != (nint)L)
                    lua_xmove(state.L, L, 1);
                break;
            case Kind.Nil:
            default:
                lua_pushnil(L);
                break;
        }
    }

    /// <summary>
    /// Captures this value without touching the state, so it can be pushed later by another thread's turn.
    /// Reference-backed values stay owned by the caller and must stay alive until they are pushed.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown for borrowed views, which end with their callback.</exception>
    internal IntoLuauCopied CaptureBorrowed()
    {
        switch (Type)
        {
            case Kind.Value:
                return IntoLuauCopied.FromBorrowedValue(_luauValue);
            case Kind.Reference:
                return _slot.Reference is { } reference
                    ? IntoLuauCopied.FromBorrowedReference(reference)
                    : throw new InvalidOperationException(
                        "A borrowed view cannot be passed to an async call that waits for another thread using the state."
                    );
            default:
                return CaptureCopied();
        }
    }

    /// <summary> Captures <paramref name="values"/> with <see cref="CaptureBorrowed()"/>. </summary>
    internal static IntoLuauCopied[] CaptureBorrowed(scoped in RefEnumerable<IntoLuau> values)
    {
        var copies = new IntoLuauCopied[values.Length];
        for (int i = 0; i < copies.Length; i++)
            copies[i] = values[i].CaptureBorrowed();
        return copies;
    }

    internal unsafe IntoLuauCopied CaptureCopied()
    {
        switch (Type)
        {
            case Kind.Chars:
                return IntoLuauCopied.FromString(_readOnlySpanChar.ToString());
            case Kind.Buffer:
                return IntoLuauCopied.FromBuffer(_readOnlyBuffer.ToArray());
            case Kind.Bool:
                return IntoLuauCopied.FromBool(_bool);
            case Kind.Number:
                return IntoLuauCopied.FromNumber(_number);
            case Kind.Integer:
                return IntoLuauCopied.FromNumber(_integer);
            case Kind.Unsigned:
                return IntoLuauCopied.FromNumber((uint)_integer);
            case Kind.Value:
                return IntoLuauCopied.FromValue(_luauValue.Get<LuauValue>());
            case Kind.UserdataFactory:
                Debug.Assert(_factory is not null);
                return IntoLuauCopied.FromUserdataFactory(_factory);
            case Kind.Userdata:
            {
                Debug.Assert(_userdata is not null && _pushUserdata is not null);
                object userdata = _userdata;
                Action<LuauState, object> pushUserdata = _pushUserdata;
                return IntoLuauCopied.FromUserdataFactory(state =>
                {
                    pushUserdata(state, userdata);
                    return new LuauUserdata(state, state.ReferenceTracker.TrackAndPopRef(state.L, -1));
                });
            }
            case Kind.Reference:
            {
                LuauState state = _slot.State;
                _slot.Push(state);
                using var pop = new PopDisposable(state.L, shouldPop: true);
                return IntoLuauCopied.FromValue(LuauValue.ToValue(state));
            }
            case Kind.Nil:
            default:
                return default;
        }
    }

    /// <summary> Converts to a string or nil </summary>
    /// <param name="value"> The string value </param>
    /// <returns> A temporary representation of the value </returns>
    public static implicit operator IntoLuau(string? value) =>
        value is null ? default : new IntoLuau(valueChars: value);

    /// <summary> Converts to a string </summary>
    /// <param name="value"> The string value. An empty span is an empty string, not nil. </param>
    /// <returns> A temporary representation of the value </returns>
    public static implicit operator IntoLuau(ReadOnlySpan<char> value) => new(valueChars: value);

    /// <summary> Converts to a boolean </summary>
    /// <param name="value"> The boolean value </param>
    /// <returns> A temporary representation of the value </returns>
    public static implicit operator IntoLuau(bool value) => new(valueBool: value);

    /// <summary> Converts to a boolean or nil </summary>
    /// <param name="value"> The boolean value </param>
    /// <returns> A temporary representation of the value </returns>
    public static implicit operator IntoLuau(bool? value) =>
        value is null ? default : new IntoLuau(valueBool: value.Value);

    /// <summary> Converts to a number </summary>
    /// <param name="value"> The number value </param>
    /// <returns> A temporary representation of the value </returns>
    public static implicit operator IntoLuau(Half value) => new(valueNumber: (double)value);

    /// <summary> Converts to a number </summary>
    /// <param name="value"> The number value </param>
    /// <returns> A temporary representation of the value </returns>
    public static implicit operator IntoLuau(Half? value) =>
        value is null ? default : new IntoLuau(valueNumber: (double)value.Value);

    /// <summary> Converts to a number </summary>
    /// <param name="value"> The number value </param>
    /// <returns> A temporary representation of the value </returns>
    public static implicit operator IntoLuau(double value) => new(valueNumber: value);

    /// <summary> Converts to an unsigned integer </summary>
    /// <param name="value"> The unsigned integer value </param>
    /// <returns> A temporary representation of the value </returns>
    public static implicit operator IntoLuau(byte value) => new(valueUnsigned: value);

    /// <summary> Converts to an unsigned integer </summary>
    /// <param name="value"> The unsigned integer value </param>
    /// <returns> A temporary representation of the value </returns>
    public static implicit operator IntoLuau(ushort value) => new(valueUnsigned: value);

    /// <summary> Converts to an unsigned integer </summary>
    /// <param name="value"> The unsigned integer value </param>
    /// <returns> A temporary representation of the value </returns>
    public static implicit operator IntoLuau(uint value) => new(valueUnsigned: value);

    /// <summary> Converts to an integer </summary>
    /// <param name="value"> The integer value </param>
    /// <returns> A temporary representation of the value </returns>
    public static implicit operator IntoLuau(int value) => new(valueInteger: value);

    /// <summary> Converts to a number or nil </summary>
    /// <param name="value"> The number value </param>
    /// <returns> A temporary representation of the value </returns>
    public static implicit operator IntoLuau(double? value) =>
        value is null ? default : new IntoLuau(valueNumber: value.Value);

    /// <summary> Converts to an unsigned integer </summary>
    /// <param name="value"> The unsigned integer value </param>
    /// <returns> A temporary representation of the value </returns>
    public static implicit operator IntoLuau(byte? value) =>
        value is null ? default : new IntoLuau(valueUnsigned: value.Value);

    /// <summary> Converts to an unsigned integer </summary>
    /// <param name="value"> The unsigned integer value </param>
    /// <returns> A temporary representation of the value </returns>
    public static implicit operator IntoLuau(ushort? value) =>
        value is null ? default : new IntoLuau(valueUnsigned: value.Value);

    /// <summary> Converts to an unsigned integer </summary>
    /// <param name="value"> The unsigned integer value </param>
    /// <returns> A temporary representation of the value </returns>
    public static implicit operator IntoLuau(uint? value) =>
        value is null ? default : new IntoLuau(valueUnsigned: value.Value);

    /// <summary> Converts to an integer </summary>
    /// <param name="value"> The integer value </param>
    /// <returns> A temporary representation of the value </returns>
    public static implicit operator IntoLuau(int? value) =>
        value is null ? default : new IntoLuau(valueInteger: value.Value);

    /// <summary> Converts to a Luau value </summary>
    /// <param name="value"> The Luau value </param>
    /// <returns> A temporary representation of the value </returns>
    public static implicit operator IntoLuau(LuauValue value) => new(value);

    /// <summary> Converts to a buffer or nil </summary>
    /// <param name="value"> The byte array </param>
    /// <returns> A temporary representation of the value </returns>
    public static implicit operator IntoLuau(byte[]? value) => value is null ? default : new IntoLuau(value);

    /// <summary> Converts to a userdata </summary>
    /// <param name="userdata"> The userdata </param>
    /// <typeparam name="T"> The type of the userdata </typeparam>
    /// <returns> A temporary representation of the value </returns>
    /// <exception cref="ArgumentNullException"> Thrown when <paramref name="userdata"/> is null. </exception>
    public static IntoLuau FromUserdata<T>(T userdata)
        where T : class, ILuauUserdata<T>
    {
        ArgumentNullException.ThrowIfNull(userdata);
        return new IntoLuau(userdata, UserdataPusher<T>.Push);
    }

    // One delegate per type, so that converting a userdata allocates nothing
    private static class UserdataPusher<T>
        where T : class, ILuauUserdata<T>
    {
        public static readonly Action<LuauState, object> Push = static (state, userdata) =>
            state.PushUserdata((T)userdata);
    }

    /// <summary> Converts to the userdata that <paramref name="factory"/> creates when the value is pushed. </summary>
    /// <param name="factory">
    /// Creates a reference for this push, for example with <see cref="LuauState.GetOrCreateUserdata{T}"/>. The
    /// reference is disposed after the userdata was pushed, so do not return one that you keep using: pass a
    /// <see cref="LuauUserdata"/> you own directly instead.
    /// </param>
    /// <returns> A temporary representation of the value </returns>
    public static IntoLuau FromUserdata(Func<LuauState, LuauUserdata> factory) => new(factory);
}
