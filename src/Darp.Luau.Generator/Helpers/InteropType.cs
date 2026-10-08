namespace Darp.Luau.Generator.Helpers;

internal readonly record struct InteropType(
    LuauInteropKind Type,
    bool IsNullable,
    string? OriginalTypeName,
    bool IsGeneratedUserdata = false,
    string? TupleElementName = null,
    string? EnumUnderlyingTypeName = null
);
