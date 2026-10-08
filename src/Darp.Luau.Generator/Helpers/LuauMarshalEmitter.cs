namespace Darp.Luau.Generator.Helpers;

internal static class LuauMarshalEmitter
{
    public static string GenerateParameterRead(int parameterIndex, InteropType param)
    {
        return GenerateParameterRead(
            parameterIndex,
            param.Type,
            param.IsNullable,
            param.OriginalTypeName,
            GetDotnetType(param),
            param.EnumUnderlyingTypeName
        );
    }

    public static string GenerateSingleArgumentRead(string variableName, InteropType param)
    {
        return GenerateSingleArgumentRead(
            variableName,
            param.Type,
            param.IsNullable,
            param.OriginalTypeName,
            GetDotnetType(param),
            param.EnumUnderlyingTypeName
        );
    }

    public static string FormatIntoLuauExpression(string valueExpression, InteropType mapping)
    {
        return FormatIntoLuauExpression(valueExpression, mapping.Type, mapping.IsNullable);
    }

    private static string GenerateParameterRead(
        int parameterIndex,
        LuauInteropKind type,
        bool isNullable,
        string? originalTypeName,
        string dotnetType,
        string? enumUnderlyingTypeName
    )
    {
        return type switch
        {
            LuauInteropKind.Boolean => isNullable
                ? $"""
                    if (!args.TryReadBooleanOrNil(parameterIndex: {parameterIndex}, out {dotnetType} a{parameterIndex}, out error))
                        return global::Darp.Luau.LuauReturn.Error(error);
                    """
                : $"""
                    if (!args.TryReadBoolean(parameterIndex: {parameterIndex}, out {dotnetType} a{parameterIndex}, out error))
                        return global::Darp.Luau.LuauReturn.Error(error);
                    """,
            LuauInteropKind.String => isNullable
                ? $"""
                    if (!args.TryReadUtf8StringOrNil(parameterIndex: {parameterIndex}, out {dotnetType} a{parameterIndex}, out var isNil, out error))
                        return global::Darp.Luau.LuauReturn.Error(error);
                    """
                : $"""
                    if (!args.TryReadUtf8String(parameterIndex: {parameterIndex}, out {dotnetType} a{parameterIndex}, out error))
                        return global::Darp.Luau.LuauReturn.Error(error);
                    """,
            LuauInteropKind.StringCharSpan => $"""
                if (!args.TryReadUtf8String(parameterIndex: {parameterIndex}, out global::System.ReadOnlySpan<byte> a{parameterIndex}Raw, out error))
                    return global::Darp.Luau.LuauReturn.Error(error);
                int a{parameterIndex}CharCount = global::System.Text.Encoding.UTF8.GetCharCount(a{parameterIndex}Raw);
                global::System.Span<char> a{parameterIndex} = a{parameterIndex}CharCount <= 256 ? stackalloc char[a{parameterIndex}CharCount] : new char[a{parameterIndex}CharCount];
                _ = global::System.Text.Encoding.UTF8.GetChars(a{parameterIndex}Raw, a{parameterIndex});
                """,
            LuauInteropKind.StringString => isNullable
                ? $"""
                    if (!args.TryReadUtf8StringOrNil(parameterIndex: {parameterIndex}, out global::System.ReadOnlySpan<byte> a{parameterIndex}Raw, out bool a{parameterIndex}IsNil, out error))
                        return global::Darp.Luau.LuauReturn.Error(error);
                    {dotnetType} a{parameterIndex} = a{parameterIndex}IsNil ? null : global::System.Text.Encoding.UTF8.GetString(a{parameterIndex}Raw);
                    """
                : $"""
                    if (!args.TryReadUtf8String(parameterIndex: {parameterIndex}, out global::System.ReadOnlySpan<byte> a{parameterIndex}Raw, out error))
                        return global::Darp.Luau.LuauReturn.Error(error);
                    string a{parameterIndex} = global::System.Text.Encoding.UTF8.GetString(a{parameterIndex}Raw);
                    """,
            LuauInteropKind.Number
            or LuauInteropKind.NumberByte
            or LuauInteropKind.NumberUShort
            or LuauInteropKind.NumberUInt
            or LuauInteropKind.NumberULong
            or LuauInteropKind.NumberUInt128
            or LuauInteropKind.NumberSByte
            or LuauInteropKind.NumberShort
            or LuauInteropKind.NumberInt
            or LuauInteropKind.NumberLong
            or LuauInteropKind.NumberInt128
            or LuauInteropKind.NumberHalf
            or LuauInteropKind.NumberFloat
            or LuauInteropKind.NumberDecimal => isNullable
                ? $"""
                    if (!args.TryReadNumberOrNil(parameterIndex: {parameterIndex}, out {dotnetType} a{parameterIndex}, out error))
                        return global::Darp.Luau.LuauReturn.Error(error);
                    """
                : $"""
                    if (!args.TryReadNumber(parameterIndex: {parameterIndex}, out {dotnetType} a{parameterIndex}, out error))
                        return global::Darp.Luau.LuauReturn.Error(error);
                    """,
            LuauInteropKind.Enum => isNullable
                ? $"""
                    if (!args.TryReadNumberOrNil(parameterIndex: {parameterIndex}, out {enumUnderlyingTypeName}? a{parameterIndex}Raw, out error))
                        return global::Darp.Luau.LuauReturn.Error(error);
                    {dotnetType} a{parameterIndex} = a{parameterIndex}Raw.HasValue ? ({originalTypeName})a{parameterIndex}Raw.Value : null;
                    """
                : $"""
                    if (!args.TryReadNumber(parameterIndex: {parameterIndex}, out {enumUnderlyingTypeName} a{parameterIndex}Raw, out error))
                        return global::Darp.Luau.LuauReturn.Error(error);
                    {dotnetType} a{parameterIndex} = ({originalTypeName})a{parameterIndex}Raw;
                    """,
            LuauInteropKind.ManagedUserdata => isNullable
                ? $"""
                    if (!args.TryReadUserdataOrNil<{originalTypeName}>(parameterIndex: {parameterIndex}, out {dotnetType} a{parameterIndex}, out error))
                        return global::Darp.Luau.LuauReturn.Error(error);
                    """
                : $"""
                    if (!args.TryReadUserdata<{originalTypeName}>(parameterIndex: {parameterIndex}, out {dotnetType}? a{parameterIndex}, out error))
                        return global::Darp.Luau.LuauReturn.Error(error);
                    """,
            LuauInteropKind.LuauValue
            or LuauInteropKind.LuauTableView
            or LuauInteropKind.LuauStringView
            or LuauInteropKind.LuauFunctionView
            or LuauInteropKind.LuauBufferView
            or LuauInteropKind.LuauUserdataView => $"""
                if (!args.{GetTryFunction(
                    type
                )}(parameterIndex: {parameterIndex}, out {dotnetType} a{parameterIndex}, out error))
                    return global::Darp.Luau.LuauReturn.Error(error);
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Could not generate parameter reads"),
        };
    }

    private static string GenerateSingleArgumentRead(
        string variableName,
        LuauInteropKind type,
        bool isNullable,
        string? originalTypeName,
        string dotnetType,
        string? enumUnderlyingTypeName
    )
    {
        return type switch
        {
            LuauInteropKind.Boolean => isNullable
                ? $"""
                    if (!args.TryReadBooleanOrNil(out {dotnetType} {variableName}, out string? error))
                        return global::Darp.Luau.LuauOutcome.Error(error);
                    """
                : $"""
                    if (!args.TryReadBoolean(out {dotnetType} {variableName}, out string? error))
                        return global::Darp.Luau.LuauOutcome.Error(error);
                    """,
            LuauInteropKind.StringString => isNullable
                ? $"""
                    if (!args.TryReadUtf8StringOrNil(out {dotnetType} {variableName}, out string? error))
                        return global::Darp.Luau.LuauOutcome.Error(error);
                    """
                : $"""
                    if (!args.TryReadUtf8String(out {dotnetType}? {variableName}, out string? error))
                        return global::Darp.Luau.LuauOutcome.Error(error);
                    """,
            LuauInteropKind.Number
            or LuauInteropKind.NumberByte
            or LuauInteropKind.NumberUShort
            or LuauInteropKind.NumberUInt
            or LuauInteropKind.NumberULong
            or LuauInteropKind.NumberUInt128
            or LuauInteropKind.NumberSByte
            or LuauInteropKind.NumberShort
            or LuauInteropKind.NumberInt
            or LuauInteropKind.NumberLong
            or LuauInteropKind.NumberInt128
            or LuauInteropKind.NumberHalf
            or LuauInteropKind.NumberFloat
            or LuauInteropKind.NumberDecimal => isNullable
                ? $"""
                    if (!args.TryReadNumberOrNil(out {dotnetType} {variableName}, out string? error))
                        return global::Darp.Luau.LuauOutcome.Error(error);
                    """
                : $"""
                    if (!args.TryReadNumber(out {dotnetType} {variableName}, out string? error))
                        return global::Darp.Luau.LuauOutcome.Error(error);
                    """,
            LuauInteropKind.Enum => isNullable
                ? $"""
                    if (!args.TryReadNumberOrNil(out {enumUnderlyingTypeName}? {variableName}Raw, out string? error))
                        return global::Darp.Luau.LuauOutcome.Error(error);
                    {dotnetType} {variableName} = {variableName}Raw.HasValue ? ({originalTypeName}){variableName}Raw.Value : null;
                    """
                : $"""
                    if (!args.TryReadNumber(out {enumUnderlyingTypeName} {variableName}Raw, out string? error))
                        return global::Darp.Luau.LuauOutcome.Error(error);
                    {dotnetType} {variableName} = ({originalTypeName}){variableName}Raw;
                    """,
            LuauInteropKind.ManagedUserdata => isNullable
                ? $"""
                    if (!args.TryReadUserdataOrNil<{originalTypeName}>(out {dotnetType} {variableName}, out string? error))
                        return global::Darp.Luau.LuauOutcome.Error(error);
                    """
                : $"""
                    if (!args.TryReadUserdata<{originalTypeName}>(out {dotnetType}? {variableName}, out string? error))
                        return global::Darp.Luau.LuauOutcome.Error(error);
                    """,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Could not generate single argument reads"),
        };
    }

    private static string GetTryFunction(LuauInteropKind type)
    {
        return type switch
        {
            LuauInteropKind.Boolean => "TryReadBoolean",
            LuauInteropKind.String or LuauInteropKind.StringCharSpan or LuauInteropKind.StringString =>
                "TryReadUtf8String",
            LuauInteropKind.Number
            or LuauInteropKind.NumberByte
            or LuauInteropKind.NumberUShort
            or LuauInteropKind.NumberUInt
            or LuauInteropKind.NumberULong
            or LuauInteropKind.NumberUInt128
            or LuauInteropKind.NumberSByte
            or LuauInteropKind.NumberShort
            or LuauInteropKind.NumberInt
            or LuauInteropKind.NumberLong
            or LuauInteropKind.NumberInt128
            or LuauInteropKind.NumberHalf
            or LuauInteropKind.NumberFloat
            or LuauInteropKind.NumberDecimal
            or LuauInteropKind.Enum => "TryReadNumber",
            LuauInteropKind.LuauValue => "TryReadLuauValue",
            LuauInteropKind.LuauTableView => "TryReadLuauTable",
            LuauInteropKind.LuauFunctionView => "TryReadLuauFunction",
            LuauInteropKind.LuauStringView => "TryReadLuauString",
            LuauInteropKind.LuauBufferView => "TryReadLuauBuffer",
            LuauInteropKind.LuauUserdataView => "TryReadLuauUserdata",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Could not get the TryRead function"),
        };
    }

    private static string FormatIntoLuauExpression(string valueExpression, LuauInteropKind type, bool isNullable)
    {
        return type switch
        {
            LuauInteropKind.NumberDecimal or LuauInteropKind.NumberUInt128 or LuauInteropKind.NumberInt128 => isNullable
                ? $"(double?){valueExpression}"
                : $"(double){valueExpression}",
            LuauInteropKind.Enum => isNullable ? $"(double?){valueExpression}" : $"(double){valueExpression}",
            LuauInteropKind.ManagedUserdata => isNullable
                ? $"{valueExpression} is null ? default(global::Darp.Luau.IntoLuau) : global::Darp.Luau.IntoLuau.FromUserdata({valueExpression})"
                : $"global::Darp.Luau.IntoLuau.FromUserdata({valueExpression})",
            _ => valueExpression,
        };
    }

    private static string GetDotnetType(InteropType param)
    {
        if (param is { Type: LuauInteropKind.Enum or LuauInteropKind.ManagedUserdata, OriginalTypeName: { } name })
            return param.IsNullable ? $"{name}?" : name;

        return EmitterHelper.GetDotnetType((param.Type, param.IsNullable));
    }
}
