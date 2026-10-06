using System.Diagnostics;
using System.Text;
using Darp.Luau.Native;
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
    }

    private readonly Kind _type;
    private readonly bool _bool;
    private readonly double _number;
    private readonly int _integer;
    private readonly string? _string;
    private readonly byte[]? _buffer;
    private readonly LuauValue _value;
    private readonly Func<LuauState, LuauUserdata>? _factory;

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

    internal static IntoLuauCopied FromBool(bool value) => new(value);

    internal static IntoLuauCopied FromNumber(double value) => new(value);

    internal static IntoLuauCopied FromInteger(int value) => new(value);

    internal static IntoLuauCopied FromUnsigned(uint value) => new(value);

    internal static IntoLuauCopied FromString(string value) => new(value);

    internal static IntoLuauCopied FromBuffer(byte[] value) => new(value);

    internal static IntoLuauCopied FromValue(LuauValue value) => new(value);

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
                _value.Push(L);
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
