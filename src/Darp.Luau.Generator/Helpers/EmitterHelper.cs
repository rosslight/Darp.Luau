namespace Darp.Luau.Generator.Helpers;

internal static class EmitterHelper
{
    internal static string GetDotnetType(InteropType param)
    {
        if (param is { Type: LuauInteropKind.Enum or LuauInteropKind.ManagedUserdata, OriginalTypeName: { } name })
            return param.IsNullable ? $"{name}?" : name;
        return GetDotnetType(param.Type, param.IsNullable);
    }

    internal static string GetTupleReturnType(InteropType param, int index)
    {
        string type = GetDotnetType(param);
        string defaultName = $"Item{index}";
        return param.TupleElementName is { Length: > 0 } name && name != defaultName ? $"{type} {name}" : type;
    }

    internal static string GetDotnetType((LuauInteropKind Type, bool IsNullable) tuple) =>
        GetDotnetType(tuple.Type, tuple.IsNullable);

    private static string GetDotnetType(LuauInteropKind type, bool isNullable)
    {
        string dotnetType = type switch
        {
            LuauInteropKind.Boolean => "bool",
            LuauInteropKind.String => "global::System.ReadOnlySpan<byte>",
            LuauInteropKind.StringCharSpan => "global::System.ReadOnlySpan<char>",
            LuauInteropKind.StringString => "string",
            LuauInteropKind.Number => "double",
            LuauInteropKind.NumberByte => "byte",
            LuauInteropKind.NumberUShort => "ushort",
            LuauInteropKind.NumberUInt => "uint",
            LuauInteropKind.NumberULong => "ulong",
            LuauInteropKind.NumberUInt128 => "global::System.UInt128",
            LuauInteropKind.NumberSByte => "sbyte",
            LuauInteropKind.NumberShort => "short",
            LuauInteropKind.NumberInt => "int",
            LuauInteropKind.NumberLong => "long",
            LuauInteropKind.NumberInt128 => "global::System.Int128",
            LuauInteropKind.NumberHalf => "global::System.Half",
            LuauInteropKind.NumberFloat => "float",
            LuauInteropKind.NumberDecimal => "decimal",
            LuauInteropKind.LuauValue => "global::Darp.Luau.LuauValue",
            LuauInteropKind.LuauTableView => "global::Darp.Luau.LuauTableView",
            LuauInteropKind.LuauFunctionView => "global::Darp.Luau.LuauFunctionView",
            LuauInteropKind.LuauStringView => "global::Darp.Luau.LuauStringView",
            LuauInteropKind.LuauBufferView => "global::Darp.Luau.LuauBufferView",
            LuauInteropKind.LuauUserdataView => "global::Darp.Luau.LuauUserdataView",
            LuauInteropKind.CancellationToken => "global::System.Threading.CancellationToken",
            LuauInteropKind.Enum or LuauInteropKind.ManagedUserdata => throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                "Use GetDotnetType(ParameterTypeInfo) for enum and managed userdata types"
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Could not get the dotnet type"),
        };
        return isNullable ? $"{dotnetType}?" : dotnetType;
    }

    public static string GetFunctionRepresentation(InteropSignature signature)
    {
        string parameters = string.Join(",", signature.Parameters.Select(GetDotnetType));
        return (signature.Parameters.Length, GetReturnType(signature)) switch
        {
            (0, null) => "global::System.Action",
            (_, null) => $"global::System.Action<{parameters}>",
            (0, { } returnType) => $"global::System.Func<{returnType}>",
            (_, { } returnType) => $"global::System.Func<{parameters}, {returnType}>",
        };
    }

    /// <summary> The C# return type of a callback with this signature, or <c>null</c> when it returns nothing. </summary>
    public static string? GetReturnType(InteropSignature signature)
    {
        string? resultType = GetResultType(signature);
        string? awaitable = signature.Awaitable switch
        {
            AwaitableReturnKind.Task => "global::System.Threading.Tasks.Task",
            AwaitableReturnKind.ValueTask => "global::System.Threading.Tasks.ValueTask",
            _ => null,
        };
        if (awaitable is null)
            return resultType;
        return resultType is null ? awaitable : $"{awaitable}<{resultType}>";
    }

    /// <summary> The C# type of the values Luau receives, or <c>null</c> when it receives nothing. </summary>
    public static string? GetResultType(InteropSignature signature)
    {
        ImmutableEquatableArray<InteropType> returnTypes = signature.ReturnTypes;
        return returnTypes.Length switch
        {
            0 => null,
            1 => GetDotnetType(returnTypes[0]),
            _ => $"({string.Join(", ", returnTypes.Select((x, i) => GetTupleReturnType(x, i + 1)))})",
        };
    }
}
