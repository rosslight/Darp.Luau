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
        StackReference,
        TrackedReference,
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
    private readonly StackReference _stackReference;
    private readonly RegistryReferenceTracker.TrackedReference? _trackedReference;

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

    private IntoLuau(StackReference stackReference)
    {
        Type = Kind.StackReference;
        _stackReference = stackReference;
    }

    private IntoLuau(RegistryReferenceTracker.TrackedReference trackedReference)
    {
        Type = Kind.TrackedReference;
        _trackedReference = trackedReference;
    }

    internal static IntoLuau FromRefSource(StackReference stackReference) => new(stackReference);

    internal static IntoLuau Borrow(LuauState? state, ulong handle)
    {
        if (state is null)
            return default;
        if (!state.TryGetTrackedReference(handle, out var reference))
            throw new ObjectDisposedException(nameof(handle), "Tried to access a reference that is no longer tracked.");
        return new IntoLuau(reference);
    }

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
                    fixed (byte* pStr = buffer[..numberOfBytes])
                    {
                        lua_pushlstring(L, pStr, (nuint)numberOfBytes);
                    }
                }
                else
                {
                    Span<byte> buffer = stackalloc byte[Encoding.UTF8.GetByteCount(_readOnlySpanChar)];
                    int numberOfBytes = Encoding.UTF8.GetBytes(_readOnlySpanChar, buffer);
                    fixed (byte* pStr = buffer[..numberOfBytes])
                    {
                        lua_pushlstring(L, pStr, (nuint)numberOfBytes);
                    }
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
            case Kind.StackReference:
                if (!ReferenceEquals(state, _stackReference.ValidateInternal()))
                    throw new InvalidOperationException("Cross-state reference usage is not allowed.");
#pragma warning disable CA2000 // The pushed value is intentionally transferred to the caller's stack protocol and must remain on the stack.
                _ = _stackReference.PushToTop();
#pragma warning restore CA2000
                if ((nint)state.L != (nint)L)
                    lua_xmove(state.L, L, 1);
                break;
            case Kind.TrackedReference:
                if (!ReferenceEquals(state, _trackedReference!.ValidateInternal()))
                    throw new InvalidOperationException("Cross-state reference usage is not allowed.");
#pragma warning disable CA2000 // The pushed value is intentionally transferred to the caller's stack protocol and must remain on the stack.
                _ = _trackedReference!.PushToTop();
#pragma warning restore CA2000
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
            case Kind.TrackedReference:
                Debug.Assert(_trackedReference is not null);
                return IntoLuauCopied.FromBorrowedReference(_trackedReference);
            case Kind.StackReference:
                throw new InvalidOperationException(
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

    internal IntoLuauCopied CaptureCopied()
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
                if (!_luauValue.TryGet(out LuauValue copiedValue))
                    throw new InvalidOperationException("Could not capture LuauValue for deferred return.");
                return IntoLuauCopied.FromValue(copiedValue);
            case Kind.UserdataFactory:
                Debug.Assert(_factory is not null);
                return IntoLuauCopied.FromUserdataFactory(_factory);
            case Kind.StackReference:
            {
                LuauState state = _stackReference.ValidateInternal();
                using PopDisposable pop = _stackReference.PushToStack(out int stackIndex);
                return IntoLuauCopied.FromValue(LuauValue.ToValue(state, stackIndex));
            }
            case Kind.TrackedReference:
            {
                Debug.Assert(_trackedReference is not null);
                LuauState state = _trackedReference.ValidateInternal();
                using PopDisposable pop = _trackedReference.PushToTop();
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

    /// <summary> Converts to a string or nil </summary>
    /// <param name="value"> The string value </param>
    /// <returns> A temporary representation of the value </returns>
    public static implicit operator IntoLuau(ReadOnlySpan<char> value) =>
        value.IsEmpty ? default : new IntoLuau(valueChars: value);

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

    /// <summary> Converts to a buffer </summary>
    /// <param name="value"> The byte array </param>
    /// <returns> A temporary representation of the value </returns>
    public static implicit operator IntoLuau(byte[] value) => new(value);

    /// <summary> Converts to a userdata </summary>
    /// <param name="userdata"> The userdata </param>
    /// <typeparam name="T"> The type of the userdata </typeparam>
    /// <returns> A temporary representation of the value </returns>
    public static IntoLuau FromUserdata<T>(T userdata)
        where T : class, ILuauUserData<T> => new(state => state.GetOrCreateUserdata(userdata));

    /// <summary> Converts to a userdata </summary>
    /// <param name="factory"> The userdata factory </param>
    /// <returns> A temporary representation of the value </returns>
    public static IntoLuau FromUserdata(Func<LuauState, LuauUserdata> factory) => new(factory);
}
