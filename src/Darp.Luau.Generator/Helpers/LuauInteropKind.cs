namespace Darp.Luau.Generator.Helpers;

internal enum LuauInteropKind
{
    Boolean, // bool

    String, // -> ReadOnlySpan<byte>
    StringCharSpan, // -> ReadOnlySpan<char>
    StringString, // -> string

    Number, // -> double
    NumberByte, // -> byte
    NumberUShort, // -> ushort
    NumberUInt, // -> uint
    NumberULong, // -> ulong
    NumberUInt128, // -> System.UInt128
    NumberSByte, // -> sbyte
    NumberShort, // -> short
    NumberInt, // -> int
    NumberLong, // -> long
    NumberInt128, // -> System.Int128
    NumberHalf, // -> System.Half
    NumberFloat, // -> float
    NumberDecimal, // -> decimal

    Enum, // C# enum -> number in Lua

    LuauValue,
    LuauTableView,
    LuauFunctionView,
    LuauStringView,
    LuauBufferView,
    LuauUserdataView,
    ManagedUserdata,

    CancellationToken, // no Luau argument: the token of the async host call
}
