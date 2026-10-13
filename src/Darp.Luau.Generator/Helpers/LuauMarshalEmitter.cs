namespace Darp.Luau.Generator.Helpers;

internal static class LuauMarshalEmitter
{
    /// <summary> Writes the statements that read argument <paramref name="parameterIndex"/> into a local <c>a{index}</c>. </summary>
    /// <remarks> A read that fails throws. The callback boundary turns that into the Luau error of the script. </remarks>
    public static string GenerateParameterRead(int parameterIndex, InteropType param)
    {
        string argument = $"args[{parameterIndex}]";
        string local = $"a{parameterIndex}";
        if (param.Type is LuauInteropKind.StringCharSpan)
        {
            // Luau holds UTF-8. The chars live as long as the call, on the stack when the text is short.
            return $"""
                global::System.ReadOnlySpan<byte> {local}Raw = {argument}.Get<global::System.ReadOnlySpan<byte>>();
                int {local}CharCount = global::System.Text.Encoding.UTF8.GetCharCount({local}Raw);
                global::System.Span<char> {local} = {local}CharCount <= 256 ? stackalloc char[{local}CharCount] : new char[{local}CharCount];
                _ = global::System.Text.Encoding.UTF8.GetChars({local}Raw, {local});
                """;
        }

        return $"{GetDotnetType(param)} {local} = {FormatRead(argument, param)};";
    }

    /// <summary> Writes the expression that reads <paramref name="valueView"/>, a <c>LuauValueView</c>, as <paramref name="param"/>. </summary>
    public static string FormatRead(string valueView, InteropType param)
    {
        // A reference type cannot say through its type that nil is fine: string? and string are one type at run time.
        if (param is { IsNullable: true, Type: LuauInteropKind.StringString or LuauInteropKind.ManagedUserdata })
            return $"{valueView}.GetOrNull<{GetDotnetType(param with { IsNullable = false })}>()";

        return $"{valueView}.Get<{GetDotnetType(param)}>()";
    }

    public static string FormatIntoLuauExpression(string valueExpression, InteropType mapping)
    {
        return mapping.Type switch
        {
            LuauInteropKind.NumberDecimal or LuauInteropKind.NumberUInt128 or LuauInteropKind.NumberInt128 =>
                mapping.IsNullable ? $"(double?){valueExpression}" : $"(double){valueExpression}",
            LuauInteropKind.Enum => mapping.IsNullable ? $"(double?){valueExpression}" : $"(double){valueExpression}",
            LuauInteropKind.ManagedUserdata => mapping.IsNullable
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
