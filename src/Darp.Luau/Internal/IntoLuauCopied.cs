using System.Diagnostics;
using System.Text;
using Darp.Luau.Native;
using Darp.Luau.Utils;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Internal;

internal readonly struct IntoLuauCopied
{
    private enum Kind
    {
        Nil = 0,
        Bool,
        Number,
        Integer,
        Unsigned,
        String,
        Buffer,
        Value,
        UserdataFactory,
        BorrowedValue,
        BorrowedReference,
    }

    private readonly Kind _type;
    private readonly bool _bool;
    private readonly double _number;
    private readonly int _integer;
    private readonly string? _string;
    private readonly byte[]? _buffer;
    private readonly LuauValue _value;
    private readonly Func<LuauState, LuauUserdata>? _factory;
    private readonly RegistryReferenceTracker.TrackedReference? _reference;

    private IntoLuauCopied(bool valueBool) => (_type, _bool) = (Kind.Bool, valueBool);

    private IntoLuauCopied(double valueNumber) => (_type, _number) = (Kind.Number, valueNumber);

    private IntoLuauCopied(int valueInteger) => (_type, _integer) = (Kind.Integer, valueInteger);

    private IntoLuauCopied(uint valueUnsigned) => (_type, _integer) = (Kind.Unsigned, (int)valueUnsigned);

    private IntoLuauCopied(string valueString)
    {
        _type = Kind.String;
        _string = valueString;
    }

    private IntoLuauCopied(byte[] valueBuffer)
    {
        _type = Kind.Buffer;
        _buffer = valueBuffer;
    }

    private IntoLuauCopied(LuauValue value)
    {
        _type = Kind.Value;
        _value = value;
    }

    private IntoLuauCopied(Func<LuauState, LuauUserdata> factory)
    {
        _type = Kind.UserdataFactory;
        _factory = factory;
    }

    private IntoLuauCopied(LuauValue value, bool isOwned)
    {
        _type = isOwned ? Kind.Value : Kind.BorrowedValue;
        _value = value;
    }

    private IntoLuauCopied(RegistryReferenceTracker.TrackedReference reference)
    {
        _type = Kind.BorrowedReference;
        _reference = reference;
    }

    internal static IntoLuauCopied FromBool(bool value) => new(value);

    internal static IntoLuauCopied FromNumber(double value) => new(value);

    internal static IntoLuauCopied FromInteger(int value) => new(value);

    internal static IntoLuauCopied FromUnsigned(uint value) => new(value);

    internal static IntoLuauCopied FromString(string value) => new(value);

    internal static IntoLuauCopied FromBuffer(byte[] value) => new(value);

    internal static IntoLuauCopied FromValue(LuauValue value) => new(value);

    /// <summary> Refers to a value the caller keeps owning; it must stay alive until the copy is pushed. </summary>
    internal static IntoLuauCopied FromBorrowedValue(LuauValue value) => new(value, isOwned: false);

    /// <summary> Refers to a reference the caller keeps owning; it must stay alive until the copy is pushed. </summary>
    internal static IntoLuauCopied FromBorrowedReference(RegistryReferenceTracker.TrackedReference reference) =>
        new(reference);

    internal static IntoLuauCopied FromUserdataFactory(Func<LuauState, LuauUserdata> factory) => new(factory);

    internal unsafe void Push(LuauState state, lua_State* L)
    {
        if (!state.OwnsThread(L))
            throw new InvalidOperationException("Cross-state value push is not allowed.");

        switch (_type)
        {
            case Kind.String:
                Debug.Assert(_string is not null);
                if (_string.Length > 256)
                {
                    Span<byte> utf8 = new byte[Encoding.UTF8.GetByteCount(_string)];
                    int length = Encoding.UTF8.GetBytes(_string, utf8);
                    fixed (byte* pStr = utf8[..length])
                    {
                        lua_pushlstring(L, pStr, (nuint)length);
                    }
                }
                else
                {
                    Span<byte> utf8 = stackalloc byte[Encoding.UTF8.GetByteCount(_string)];
                    int length = Encoding.UTF8.GetBytes(_string, utf8);
                    fixed (byte* pStr = utf8[..length])
                    {
                        lua_pushlstring(L, pStr, (nuint)length);
                    }
                }
                break;
            case Kind.Buffer:
                Debug.Assert(_buffer is not null);
                void* pDest = lua_newbuffer(L, (nuint)_buffer.Length);
                var destination = new Span<byte>(pDest, _buffer.Length);
                _buffer.CopyTo(destination);
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
            case Kind.BorrowedValue:
                _value.Push(L);
                break;
            case Kind.BorrowedReference:
                Debug.Assert(_reference is not null);
                if (!ReferenceEquals(state, _reference.ValidateInternal()))
                    throw new InvalidOperationException("Cross-state reference usage is not allowed.");
                if (!_reference.IsTracked)
                    throw new ObjectDisposedException(
                        nameof(LuauValue),
                        "The argument was disposed before it was used."
                    );
#pragma warning disable CA2000 // The pushed value is intentionally transferred to the caller's stack protocol and must remain on the stack.
                _ = _reference.PushToTop();
#pragma warning restore CA2000
                if ((nint)state.L != (nint)L)
                    lua_xmove(state.L, L, 1);
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
                    IntoLuau value = userdata;
                    value.Push(state, L);
                }
                finally
                {
                    userdata.Dispose();
                }
                break;
            case Kind.Nil:
            default:
                lua_pushnil(L);
                break;
        }
    }

    internal void Release()
    {
        if (_type != Kind.Value)
            return;
        _value.Dispose();
    }
}
