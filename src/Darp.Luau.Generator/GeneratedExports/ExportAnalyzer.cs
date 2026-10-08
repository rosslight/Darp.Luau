using System.Collections.Immutable;
using Darp.Luau.Generator.Helpers;
using Microsoft.CodeAnalysis;

namespace Darp.Luau.Generator.GeneratedExports;

internal static class ExportAnalyzer
{
    public static GeneratedExportsTypeAnalysis AnalyzeType(
        INamedTypeSymbol type,
        LuauExportedTypeKind expectedKind,
        Compilation compilation
    )
    {
        var diagnostics = new List<Diagnostic>();
        LuauApiSymbols? context = LuauApiSymbols.Create(compilation);
        if (context is null)
            return new GeneratedExportsTypeAnalysis(null, diagnostics.ToImmutableArray(), CanEmitSource: false);

        DiscoveredExportType? discoveredType = ExportDiscovery.DiscoverType(
            type,
            expectedKind,
            context,
            diagnostics
        );
        if (discoveredType is null)
            return new GeneratedExportsTypeAnalysis(null, diagnostics.ToImmutableArray(), CanEmitSource: false);

        bool hasFatalTypeErrors = ExportValidator.ValidateTypeShape(discoveredType, context, diagnostics);
        NormalizedExportType normalizedType = Normalize(discoveredType, context, diagnostics);
        ValidatedExportType validatedType = ExportValidator.ValidateMembers(discoveredType, normalizedType, diagnostics);
        GeneratedExportSurfaceIr model = ExportProjector.Project(validatedType);
        return new GeneratedExportsTypeAnalysis(
            model,
            diagnostics.ToImmutableArray(),
            CanEmitSource: !hasFatalTypeErrors
        );
    }

    public static NormalizedExportType Normalize(
        DiscoveredExportType discoveredType,
        LuauApiSymbols context,
        List<Diagnostic> diagnostics
    )
    {
        string? moduleName = null;
        if (discoveredType.Kind == LuauExportedTypeKind.Module)
        {
            moduleName = AttributeReader.GetStringConstructorArgument(discoveredType.Attribute);
        }

        var members = new List<NormalizedExportMember>();
        foreach (DiscoveredExportMember member in discoveredType.Members)
        {
            NormalizedExportMember? normalizedMember = member switch
            {
                DiscoveredExportProperty property => NormalizeProperty(discoveredType, property, context, diagnostics),
                DiscoveredExportMethod method => NormalizeMethod(discoveredType, method, context, diagnostics),
                _ => null,
            };

            if (normalizedMember is not null)
                members.Add(normalizedMember);
        }

        return new NormalizedExportType(
            discoveredType.Symbol,
            discoveredType.Kind,
            moduleName,
            discoveredType.Origin,
            members.ToImmutableEquatableArray()
        );
    }

    private static NormalizedExportPropertyMember? NormalizeProperty(
        DiscoveredExportType discoveredType,
        DiscoveredExportProperty discoveredProperty,
        LuauApiSymbols context,
        List<Diagnostic> diagnostics
    )
    {
        IPropertySymbol property = discoveredProperty.Symbol;
        Location location = discoveredProperty.Origin.Location;
        if (property.IsIndexer)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                    location,
                    $"property '{property.Name}' is an indexer, which is not supported"
                )
            );
            return null;
        }

        if (discoveredType.Kind == LuauExportedTypeKind.Userdata && property.IsStatic)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                    location,
                    $"userdata property '{property.Name}' must be an instance member"
                )
            );
            return null;
        }

        if (discoveredType.Kind == LuauExportedTypeKind.Module && !property.IsStatic)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                    location,
                    $"instance module property '{property.Name}' is not supported in v1 because live property generation is not implemented"
                )
            );
            return null;
        }

        string? exportedName = AttributeReader.GetStringConstructorArgument(
            discoveredProperty.Attribute
        );
        if (exportedName is null)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                    location,
                    $"member '{property.Name}' must specify a Luau-facing name"
                )
            );
            return null;
        }

        if (
            !TryGetPropertyAccess(
                discoveredProperty.Attribute,
                location,
                diagnostics,
                out LuauExportPropertyAccess requestedAccess
            )
        )
            return null;

        if (
            !TryNormalizePropertyContract(
                property,
                discoveredType.Kind,
                requestedAccess,
                context,
                location,
                diagnostics,
                out NormalizedPropertyContract propertyContract
            )
        )
        {
            return null;
        }

        if (discoveredType.Kind == LuauExportedTypeKind.Module && propertyContract.Setter is not null)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.ModulePropertyMustBeReadOnlyDescriptor,
                    location,
                    property.Name
                )
            );
            return null;
        }

        if (
            !TryParsePath(
                discoveredType.Kind,
                exportedName,
                property.Name,
                location,
                diagnostics,
                out ImmutableEquatableArray<string> pathSegments
            )
        )
        {
            return null;
        }

        return new NormalizedExportPropertyMember(
            property,
            property.Name,
            exportedName,
            pathSegments,
            discoveredProperty.Origin,
            propertyContract
        );
    }

    private static NormalizedExportMethodMember? NormalizeMethod(
        DiscoveredExportType discoveredType,
        DiscoveredExportMethod discoveredMethod,
        LuauApiSymbols context,
        List<Diagnostic> diagnostics
    )
    {
        IMethodSymbol method = discoveredMethod.Symbol;
        Location location = discoveredMethod.Origin.Location;
        if (method.MethodKind != MethodKind.Ordinary)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                    location,
                    $"method '{method.Name}' is not an ordinary method"
                )
            );
            return null;
        }

        if (discoveredType.Kind == LuauExportedTypeKind.Userdata && method.IsStatic)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                    location,
                    $"userdata method '{method.Name}' must be an instance member"
                )
            );
            return null;
        }

        string? exportedName = AttributeReader.GetStringConstructorArgument(
            discoveredMethod.Attribute
        );
        if (exportedName is null)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                    location,
                    $"member '{method.Name}' must specify a Luau-facing name"
                )
            );
            return null;
        }

        if (
            !TryParsePath(
                discoveredType.Kind,
                exportedName,
                method.Name,
                location,
                diagnostics,
                out ImmutableEquatableArray<string> pathSegments
            )
        )
        {
            return null;
        }

        if (
            !TryMapMethodSignature(
                method,
                discoveredType.Kind,
                context,
                location,
                diagnostics,
                out ImmutableEquatableArray<InteropType> parameters,
                out ImmutableEquatableArray<InteropType> returns,
                out AwaitableReturnKind awaitable
            )
        )
        {
            return null;
        }

        return new NormalizedExportMethodMember(
            method,
            method.Name,
            exportedName,
            pathSegments,
            discoveredMethod.Origin,
            new InteropSignature(parameters, returns, awaitable)
        );
    }

    private static bool TryNormalizePropertyContract(
        IPropertySymbol property,
        LuauExportedTypeKind exportedTypeKind,
        LuauExportPropertyAccess requestedAccess,
        LuauApiSymbols context,
        Location location,
        List<Diagnostic> diagnostics,
        out NormalizedPropertyContract propertyContract
    )
    {
        if (
            !TryResolvePropertyAccess(
                property,
                requestedAccess,
                out bool exposeGetter,
                out bool exposeSetter,
                out string? reason
            )
        )
        {
            diagnostics.Add(
                Diagnostic.Create(DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor, location, reason)
            );
            propertyContract = default!;
            return false;
        }

        NormalizedPropertyAccessor? getter = null;
        if (exposeGetter)
        {
            LuauInteropTypeUsage getterUsage =
                exportedTypeKind == LuauExportedTypeKind.Module
                    ? LuauInteropTypeUsage.ModuleProperty
                    : LuauInteropTypeUsage.UserdataPropertyGet;
            if (
                !TryMapPropertyType(
                    property,
                    getterUsage,
                    exportedTypeKind,
                    location,
                    context,
                    diagnostics,
                    out InteropType getterType
                )
            )
            {
                propertyContract = default!;
                return false;
            }

            getter = new NormalizedPropertyAccessor(getterType);
        }

        NormalizedPropertyAccessor? setter = null;
        if (exposeSetter)
        {
            LuauInteropTypeUsage setterUsage =
                exportedTypeKind == LuauExportedTypeKind.Module
                    ? LuauInteropTypeUsage.ModuleProperty
                    : LuauInteropTypeUsage.UserdataPropertySet;
            if (
                !TryMapPropertyType(
                    property,
                    setterUsage,
                    exportedTypeKind,
                    location,
                    context,
                    diagnostics,
                    out InteropType setterType
                )
            )
            {
                propertyContract = default!;
                return false;
            }

            setter = new NormalizedPropertyAccessor(setterType);
        }

        propertyContract = new NormalizedPropertyContract(getter, setter);
        return true;
    }

    private static bool TryMapPropertyType(
        IPropertySymbol property,
        LuauInteropTypeUsage usage,
        LuauExportedTypeKind exportedTypeKind,
        Location location,
        LuauApiSymbols context,
        List<Diagnostic> diagnostics,
        out InteropType mapping
    )
    {
        if (!TryMapExportType(property.Type, usage, context, out mapping))
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.UnsupportedGeneratedPropertyTypeDescriptor,
                    location,
                    property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    exportedTypeKind == LuauExportedTypeKind.Module ? "module property" : "userdata property",
                    property.Name
                )
            );
            mapping = default;
            return false;
        }

        return true;
    }

    private static bool TryMapMethodSignature(
        IMethodSymbol method,
        LuauExportedTypeKind exportedTypeKind,
        LuauApiSymbols context,
        Location location,
        List<Diagnostic> diagnostics,
        out ImmutableEquatableArray<InteropType> parameters,
        out ImmutableEquatableArray<InteropType> returns,
        out AwaitableReturnKind awaitable
    )
    {
        awaitable = InteropTypeMapper.GetAwaitableKind(method.ReturnType, out ITypeSymbol? awaitedType);
        if (method.IsGenericMethod)
        {
            ReportUnsupportedMethodShape(
                exportedTypeKind,
                method,
                location,
                diagnostics,
                "generic methods are not supported"
            );
            parameters = ImmutableEquatableArray<InteropType>.Empty;
            returns = ImmutableEquatableArray<InteropType>.Empty;
            return false;
        }

        if (method is { IsAsync: true, ReturnsVoid: true })
        {
            ReportUnsupportedMethodShape(
                exportedTypeKind,
                method,
                location,
                diagnostics,
                "async void methods are not supported; return Task or ValueTask so the script can wait for the work"
            );
            parameters = ImmutableEquatableArray<InteropType>.Empty;
            returns = ImmutableEquatableArray<InteropType>.Empty;
            return false;
        }

        if (method.ReturnsByRef || method.ReturnsByRefReadonly)
        {
            ReportUnsupportedMethodShape(
                exportedTypeKind,
                method,
                location,
                diagnostics,
                "by-ref returns are not supported"
            );
            parameters = ImmutableEquatableArray<InteropType>.Empty;
            returns = ImmutableEquatableArray<InteropType>.Empty;
            return false;
        }

        var parameterBuilder = new List<InteropType>();
        LuauInteropTypeUsage parameterUsage =
            exportedTypeKind == LuauExportedTypeKind.Module
                ? LuauInteropTypeUsage.ModuleFunctionParameter
                : LuauInteropTypeUsage.UserdataMethodParameter;
        foreach (IParameterSymbol parameter in method.Parameters)
        {
            if (parameter.RefKind != RefKind.None)
            {
                ReportUnsupportedMethodShape(
                    exportedTypeKind,
                    method,
                    location,
                    diagnostics,
                    $"parameter '{parameter.Name}' cannot use ref, in, or out"
                );
                parameters = ImmutableEquatableArray<InteropType>.Empty;
                returns = ImmutableEquatableArray<InteropType>.Empty;
                return false;
            }

            // A cancellation token is never a Luau argument, so its default value is never used.
            bool isCancellationToken =
                parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                == "global::System.Threading.CancellationToken";
            if ((parameter.IsOptional || parameter.HasExplicitDefaultValue) && !isCancellationToken)
            {
                ReportUnsupportedMethodShape(
                    exportedTypeKind,
                    method,
                    location,
                    diagnostics,
                    $"parameter '{parameter.Name}' is optional"
                );
                parameters = ImmutableEquatableArray<InteropType>.Empty;
                returns = ImmutableEquatableArray<InteropType>.Empty;
                return false;
            }

            if (parameter.IsParams)
            {
                ReportUnsupportedMethodShape(
                    exportedTypeKind,
                    method,
                    location,
                    diagnostics,
                    $"parameter '{parameter.Name}' uses params"
                );
                parameters = ImmutableEquatableArray<InteropType>.Empty;
                returns = ImmutableEquatableArray<InteropType>.Empty;
                return false;
            }

            if (!TryMapExportType(parameter.Type, parameterUsage, context, out InteropType parameterMapping))
            {
                ReportUnsupportedMethodShape(
                    exportedTypeKind,
                    method,
                    location,
                    diagnostics,
                    $"parameter '{parameter.Name}' has unsupported type '{parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'"
                );
                parameters = ImmutableEquatableArray<InteropType>.Empty;
                returns = ImmutableEquatableArray<InteropType>.Empty;
                return false;
            }

            parameterBuilder.Add(parameterMapping);
        }

        ITypeSymbol? returnType = awaitable is AwaitableReturnKind.None ? method.ReturnType : awaitedType;
        if (!TryMapMethodReturns(method, returnType, exportedTypeKind, context, location, diagnostics, out returns))
        {
            parameters = ImmutableEquatableArray<InteropType>.Empty;
            return false;
        }

        parameters = parameterBuilder.ToImmutableEquatableArray();
        return true;
    }

    /// <summary>
    /// Maps <paramref name="returnType"/>, the type Luau receives. It is <c>null</c> when an awaitable produces nothing.
    /// </summary>
    private static bool TryMapMethodReturns(
        IMethodSymbol method,
        ITypeSymbol? returnType,
        LuauExportedTypeKind exportedTypeKind,
        LuauApiSymbols context,
        Location location,
        List<Diagnostic> diagnostics,
        out ImmutableEquatableArray<InteropType> returns
    )
    {
        if (returnType is null or { SpecialType: SpecialType.System_Void })
        {
            returns = ImmutableEquatableArray<InteropType>.Empty;
            return true;
        }

        var returnValues = new List<InteropType>();
        LuauInteropTypeUsage returnUsage =
            exportedTypeKind == LuauExportedTypeKind.Module
                ? LuauInteropTypeUsage.ModuleFunctionReturn
                : LuauInteropTypeUsage.UserdataMethodReturn;
        if (returnType is not INamedTypeSymbol { IsTupleType: true } tupleType)
        {
            if (
                !TryMapReturnType(
                    method,
                    returnType,
                    "return value",
                    returnUsage,
                    context,
                    location,
                    diagnostics,
                    out InteropType mapping
                )
            )
            {
                returns = ImmutableEquatableArray<InteropType>.Empty;
                return false;
            }

            returnValues.Add(mapping);
            returns = returnValues.ToImmutableEquatableArray();
            return true;
        }

        if (tupleType.TupleElements.Length > 4)
        {
            ReportUnsupportedMethodShape(
                exportedTypeKind,
                method,
                location,
                diagnostics,
                "only up to 4 tuple return values are supported"
            );
            returns = ImmutableEquatableArray<InteropType>.Empty;
            return false;
        }

        foreach (IFieldSymbol tupleElement in tupleType.TupleElements)
        {
            if (tupleElement.Type is INamedTypeSymbol { IsTupleType: true })
            {
                ReportUnsupportedMethodShape(
                    exportedTypeKind,
                    method,
                    location,
                    diagnostics,
                    "nested tuple returns are not supported"
                );
                returns = ImmutableEquatableArray<InteropType>.Empty;
                return false;
            }

            string usageDescription = tupleElement.Name is { Length: > 0 }
                ? $"return tuple element '{tupleElement.Name}'"
                : "return tuple element";
            if (
                !TryMapReturnType(
                    method,
                    tupleElement.Type,
                    usageDescription,
                    returnUsage,
                    context,
                    location,
                    diagnostics,
                    out InteropType mapping
                )
            )
            {
                returns = ImmutableEquatableArray<InteropType>.Empty;
                return false;
            }

            returnValues.Add(mapping);
        }

        returns = returnValues.ToImmutableEquatableArray();
        return true;
    }

    private static bool TryMapReturnType(
        IMethodSymbol method,
        ITypeSymbol type,
        string usageDescription,
        LuauInteropTypeUsage usage,
        LuauApiSymbols context,
        Location location,
        List<Diagnostic> diagnostics,
        out InteropType mapping
    )
    {
        if (!TryMapExportType(type, usage, context, out mapping))
        {
            ReportUnsupportedMethodShape(
                usage is LuauInteropTypeUsage.ModuleFunctionReturn
                    ? LuauExportedTypeKind.Module
                    : LuauExportedTypeKind.Userdata,
                method,
                location,
                diagnostics,
                $"{usageDescription} has unsupported type '{type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'"
            );
            mapping = default;
            return false;
        }

        return true;
    }

    private static bool TryMapExportType(
        ITypeSymbol type,
        LuauInteropTypeUsage usage,
        LuauApiSymbols context,
        out InteropType mapping
    )
    {
        if (InteropTypeMapper.TryMapType(type, out mapping) && IsMappingSupportedForUsage(mapping, usage))
            return true;

        if (
            InteropTypeMapper.TryMapGeneratedUserdataType(type, context, out mapping)
            && IsMappingSupportedForUsage(mapping, usage)
        )
            return true;

        mapping = default;
        return false;
    }

    private static bool IsMappingSupportedForUsage(InteropType mapping, LuauInteropTypeUsage usage)
    {
        return InteropTypeMapper.SupportsUsage(mapping, usage)
            && (!mapping.IsNullable || InteropTypeMapper.SupportsNullableValue(mapping.Type));
    }

    private static bool TryResolvePropertyAccess(
        IPropertySymbol property,
        LuauExportPropertyAccess requestedAccess,
        out bool exposeGetter,
        out bool exposeSetter,
        out string? reason
    )
    {
        bool hasGetter = property.GetMethod is not null;
        bool hasSetter = property.SetMethod is not null;
        switch (requestedAccess)
        {
            case LuauExportPropertyAccess.Auto:
                if (hasGetter && hasSetter)
                {
                    exposeGetter = true;
                    exposeSetter = true;
                    reason = null;
                    return true;
                }

                if (hasGetter)
                {
                    exposeGetter = true;
                    exposeSetter = false;
                    reason = null;
                    return true;
                }

                if (hasSetter)
                {
                    exposeGetter = false;
                    exposeSetter = true;
                    reason = null;
                    return true;
                }

                reason = $"property '{property.Name}' must define a getter and/or setter";
                exposeGetter = false;
                exposeSetter = false;
                return false;
            case LuauExportPropertyAccess.ReadOnly:
                if (hasGetter)
                {
                    exposeGetter = true;
                    exposeSetter = false;
                    reason = null;
                    return true;
                }

                reason = $"property '{property.Name}' must define a getter for LuauPropertyAccess.ReadOnly";
                exposeGetter = false;
                exposeSetter = false;
                return false;
            case LuauExportPropertyAccess.WriteOnly:
                if (hasSetter)
                {
                    exposeGetter = false;
                    exposeSetter = true;
                    reason = null;
                    return true;
                }

                reason = $"property '{property.Name}' must define a setter for LuauPropertyAccess.WriteOnly";
                exposeGetter = false;
                exposeSetter = false;
                return false;
            case LuauExportPropertyAccess.ReadWrite:
                if (hasGetter && hasSetter)
                {
                    exposeGetter = true;
                    exposeSetter = true;
                    reason = null;
                    return true;
                }

                reason =
                    $"property '{property.Name}' must define both a getter and setter for LuauPropertyAccess.ReadWrite";
                exposeGetter = false;
                exposeSetter = false;
                return false;
            default:
                reason =
                    $"property '{property.Name}' uses an unknown LuauPropertyAccess value '{(int)requestedAccess}'";
                exposeGetter = false;
                exposeSetter = false;
                return false;
        }
    }

    private static bool TryGetPropertyAccess(
        AttributeData memberAttribute,
        Location location,
        List<Diagnostic> diagnostics,
        out LuauExportPropertyAccess access
    )
    {
        foreach (KeyValuePair<string, TypedConstant> namedArgument in memberAttribute.NamedArguments)
        {
            if (namedArgument.Key != "Access")
                continue;

            if (namedArgument.Value.Value is int rawValue && Enum.IsDefined(typeof(LuauExportPropertyAccess), rawValue))
            {
                access = (LuauExportPropertyAccess)rawValue;
                return true;
            }

            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                    location,
                    $"LuauPropertyAccess value '{namedArgument.Value.Value}' is not supported"
                )
            );
            access = default;
            return false;
        }

        access = LuauExportPropertyAccess.Auto;
        return true;
    }

    private static bool TryParsePath(
        LuauExportedTypeKind exportedTypeKind,
        string exportedName,
        string memberName,
        Location location,
        List<Diagnostic> diagnostics,
        out ImmutableEquatableArray<string> pathSegments
    )
    {
        ImmutableArray<string> rawSegments;
        bool success =
            exportedTypeKind == LuauExportedTypeKind.Module
                ? ExportPathParser.TryParseModulePath(exportedName, location, diagnostics, out rawSegments)
                : ExportPathParser.TryParseUserdataPath(
                    exportedName,
                    memberName,
                    location,
                    diagnostics,
                    out rawSegments
                );
        pathSegments = success ? rawSegments.ToImmutableEquatableArray() : ImmutableEquatableArray<string>.Empty;
        return success;
    }

    private static void ReportUnsupportedMethodShape(
        LuauExportedTypeKind exportedTypeKind,
        IMethodSymbol method,
        Location location,
        List<Diagnostic> diagnostics,
        string reason
    )
    {
        diagnostics.Add(
            Diagnostic.Create(
                DiagnosticDescriptors.UnsupportedGeneratedFunctionShapeDescriptor,
                location,
                exportedTypeKind == LuauExportedTypeKind.Module ? "module function" : "userdata method",
                method.Name,
                reason
            )
        );
    }
}
