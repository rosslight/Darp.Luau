namespace Darp.Luau.Generator.Helpers;

internal readonly record struct InteropType(
    LuauInteropKind Type,
    bool IsNullable,
    string? OriginalTypeName,
    string? TupleElementName = null,
    string? EnumUnderlyingTypeName = null
);
