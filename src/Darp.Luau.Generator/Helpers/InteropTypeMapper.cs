using Microsoft.CodeAnalysis;

namespace Darp.Luau.Generator.Helpers;

internal static class InteropTypeMapper
{
    public static bool TryMapManagedUserdataType(ITypeSymbol type, out string? typeName)
    {
        typeName = null;
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Class } namedType)
            return false;

        foreach (INamedTypeSymbol implementedInterface in namedType.AllInterfaces)
        {
            if (
                implementedInterface is not { Name: "ILuauUserData", Arity: 1, TypeArguments.Length: 1 }
                || implementedInterface.ContainingNamespace.ToDisplayString() != "Darp.Luau"
            )
            {
                continue;
            }

            if (!SymbolEqualityComparer.Default.Equals(implementedInterface.TypeArguments[0], namedType))
                continue;

            typeName = namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return true;
        }

        return false;
    }

    public static bool TryMapGeneratedUserdataType(
        ITypeSymbol type,
        LuauApiSymbols apiSymbols,
        out InteropType mapping
    )
    {
        if (
            type is not INamedTypeSymbol
            {
                TypeKind: TypeKind.Class,
                ContainingType: null,
                TypeParameters.Length: 0,
            } namedType
            || apiSymbols.GetUserdataAttribute(namedType) is null
        )
        {
            mapping = default;
            return false;
        }

        mapping = new InteropType(
            LuauInteropKind.ManagedUserdata,
            type.NullableAnnotation is NullableAnnotation.Annotated,
            namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            IsGeneratedUserdata: true
        );
        return true;
    }

    /// <summary>
    /// Recognizes <c>Task</c> and <c>ValueTask</c>. <paramref name="awaitedType"/> is the type they produce, or
    /// <c>null</c> when they produce nothing.
    /// </summary>
    public static AwaitableReturnKind GetAwaitableKind(ITypeSymbol type, out ITypeSymbol? awaitedType)
    {
        awaitedType = null;
        if (
            type is not INamedTypeSymbol { Name: "Task" or "ValueTask", Arity: 0 or 1 } namedType
            || namedType.ContainingNamespace.ToDisplayString() != "System.Threading.Tasks"
        )
        {
            return AwaitableReturnKind.None;
        }

        if (namedType.Arity == 1)
            awaitedType = namedType.TypeArguments[0];
        return namedType.Name == "Task" ? AwaitableReturnKind.Task : AwaitableReturnKind.ValueTask;
    }

    public static bool TryMapType(ITypeSymbol type, out InteropType mapping)
    {
        bool isNullable = type.NullableAnnotation is NullableAnnotation.Annotated;
        if (
            type is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
                TypeArguments.Length: 1
            } namedNullableType
        )
        {
            if (!TryMapType(namedNullableType.TypeArguments[0], out mapping))
                return false;

            mapping = mapping with { IsNullable = true };
            return true;
        }

        if (type.TypeKind == TypeKind.Enum)
        {
            mapping = new InteropType(
                LuauInteropKind.Enum,
                isNullable,
                type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                // The number is read as this type, so a value the enum cannot store is rejected.
                EnumUnderlyingTypeName: (type as INamedTypeSymbol)?.EnumUnderlyingType?.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat
                )
                    ?? "int"
            );
            return true;
        }

        if (TryMapManagedUserdataType(type, out string? userdataTypeName))
        {
            mapping = new InteropType(LuauInteropKind.ManagedUserdata, isNullable, userdataTypeName);
            return true;
        }

        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
                mapping = new InteropType(LuauInteropKind.Boolean, isNullable, null);
                return true;
            case SpecialType.System_String:
                mapping = new InteropType(LuauInteropKind.StringString, isNullable, null);
                return true;
            case SpecialType.System_Byte:
                mapping = new InteropType(LuauInteropKind.NumberByte, isNullable, null);
                return true;
            case SpecialType.System_UInt16:
                mapping = new InteropType(LuauInteropKind.NumberUShort, isNullable, null);
                return true;
            case SpecialType.System_UInt32:
                mapping = new InteropType(LuauInteropKind.NumberUInt, isNullable, null);
                return true;
            case SpecialType.System_UInt64:
                mapping = new InteropType(LuauInteropKind.NumberULong, isNullable, null);
                return true;
            case SpecialType.System_SByte:
                mapping = new InteropType(LuauInteropKind.NumberSByte, isNullable, null);
                return true;
            case SpecialType.System_Int16:
                mapping = new InteropType(LuauInteropKind.NumberShort, isNullable, null);
                return true;
            case SpecialType.System_Int32:
                mapping = new InteropType(LuauInteropKind.NumberInt, isNullable, null);
                return true;
            case SpecialType.System_Int64:
                mapping = new InteropType(LuauInteropKind.NumberLong, isNullable, null);
                return true;
            case SpecialType.System_Double:
                mapping = new InteropType(LuauInteropKind.Number, isNullable, null);
                return true;
            case SpecialType.System_Single:
                mapping = new InteropType(LuauInteropKind.NumberFloat, isNullable, null);
                return true;
            case SpecialType.System_Decimal:
                mapping = new InteropType(LuauInteropKind.NumberDecimal, isNullable, null);
                return true;
        }

        switch (type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
        {
            case "global::System.ReadOnlySpan<byte>":
                mapping = new InteropType(LuauInteropKind.String, isNullable, null);
                return true;
            case "global::System.ReadOnlySpan<char>":
                mapping = new InteropType(LuauInteropKind.StringCharSpan, isNullable, null);
                return true;
            case "global::System.Half":
                mapping = new InteropType(LuauInteropKind.NumberHalf, isNullable, null);
                return true;
            case "global::System.UInt128":
                mapping = new InteropType(LuauInteropKind.NumberUInt128, isNullable, null);
                return true;
            case "global::System.Int128":
                mapping = new InteropType(LuauInteropKind.NumberInt128, isNullable, null);
                return true;
            case "global::Darp.Luau.LuauValue":
                mapping = new InteropType(LuauInteropKind.LuauValue, isNullable, null);
                return true;
            case "global::Darp.Luau.LuauTableView":
                mapping = new InteropType(LuauInteropKind.LuauTableView, isNullable, null);
                return true;
            case "global::Darp.Luau.LuauFunctionView":
                mapping = new InteropType(LuauInteropKind.LuauFunctionView, isNullable, null);
                return true;
            case "global::Darp.Luau.LuauStringView":
                mapping = new InteropType(LuauInteropKind.LuauStringView, isNullable, null);
                return true;
            case "global::Darp.Luau.LuauBufferView":
                mapping = new InteropType(LuauInteropKind.LuauBufferView, isNullable, null);
                return true;
            case "global::Darp.Luau.LuauUserdataView":
                mapping = new InteropType(LuauInteropKind.LuauUserdataView, isNullable, null);
                return true;
            case "global::System.Threading.CancellationToken":
                mapping = new InteropType(LuauInteropKind.CancellationToken, isNullable, null);
                return true;
            default:
                mapping = default;
                return false;
        }
    }

    public static bool SupportsNullableValue(LuauInteropKind type)
    {
        return type
            is LuauInteropKind.Boolean
                or LuauInteropKind.StringString
                or LuauInteropKind.Number
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
                or LuauInteropKind.Enum
                or LuauInteropKind.ManagedUserdata;
    }

    public static bool SupportsUsage(InteropType mapping, LuauInteropTypeUsage usage)
    {
        if (mapping.Type is LuauInteropKind.CancellationToken)
        {
            return usage
                is LuauInteropTypeUsage.CreateFunctionParameter
                    or LuauInteropTypeUsage.ModuleFunctionParameter
                    or LuauInteropTypeUsage.UserdataMethodParameter;
        }

        return usage switch
        {
            LuauInteropTypeUsage.ModuleFunctionReturn or LuauInteropTypeUsage.UserdataMethodReturn =>
                SupportsReturnType(mapping.Type),
            LuauInteropTypeUsage.ModuleProperty
            or LuauInteropTypeUsage.UserdataPropertyGet
            or LuauInteropTypeUsage.UserdataPropertySet
            or LuauInteropTypeUsage.TypeFile => SupportsStoredPropertyType(mapping.Type),
            _ => true,
        };
    }

    private static bool SupportsReturnType(LuauInteropKind type) => type is not LuauInteropKind.String;

    private static bool SupportsStoredPropertyType(LuauInteropKind type)
    {
        return type
            is LuauInteropKind.Boolean
                or LuauInteropKind.StringString
                or LuauInteropKind.Number
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
                or LuauInteropKind.Enum
                or LuauInteropKind.ManagedUserdata;
    }
}
