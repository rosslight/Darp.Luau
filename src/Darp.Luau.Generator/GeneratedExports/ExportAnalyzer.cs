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

        DiscoveredExportType? discoveredType = ExportDiscovery.DiscoverType(type, expectedKind, context, diagnostics);
        if (discoveredType is null)
            return new GeneratedExportsTypeAnalysis(null, diagnostics.ToImmutableArray(), CanEmitSource: false);

        bool hasFatalTypeErrors = ExportValidator.ValidateTypeShape(discoveredType, context, diagnostics);
        NormalizedExportType normalizedType = Normalize(discoveredType, context, diagnostics);
        ValidatedExportType validatedType = ExportValidator.ValidateMembers(
            discoveredType,
            normalizedType,
            diagnostics
        );
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
        string? name = AttributeReader.GetStringConstructorArgument(discoveredType.Attribute);
        bool isModule = discoveredType.Kind == LuauExportedTypeKind.Module;

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
            ModuleName: isModule ? name : null,
            UserdataTypeName: isModule ? null : name,
            discoveredType.Origin,
            members.ToImmutableEquatableArray(),
            MetamethodAnalyzer.Analyze(discoveredType, context, diagnostics)
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

        if (!property.ExplicitInterfaceImplementations.IsEmpty)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                    location,
                    $"property '{property.Name}' is an explicit interface implementation, which generated code cannot access"
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

        string? exportedName = AttributeReader.GetStringConstructorArgument(discoveredProperty.Attribute);
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
                Diagnostic.Create(DiagnosticDescriptors.ModulePropertyMustBeReadOnlyDescriptor, location, property.Name)
            );
            return null;
        }

        // The static side of a userdata type holds the value the property had when a state first asked for it.
        if (
            discoveredType.Kind == LuauExportedTypeKind.Userdata
            && property.IsStatic
            && propertyContract is not { Getter: not null, Setter: null }
        )
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                    location,
                    $"static userdata property '{property.Name}' must be read-only because the static side holds a snapshot of its value"
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

        if (method is { IsPartialDefinition: true, PartialImplementationPart: null })
        {
            // The compiler removes calls to a partial method without an implementation. Luau would call nothing.
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                    location,
                    $"partial method '{method.Name}' has no implementation"
                )
            );
            return null;
        }

        string? exportedName = AttributeReader.GetStringConstructorArgument(discoveredMethod.Attribute);
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

        if (!TryReadSignature(method, context, location, diagnostics, out InteropSignature signature))
            return null;

        return new NormalizedExportMethodMember(
            method,
            method.Name,
            exportedName,
            pathSegments,
            discoveredMethod.Origin,
            signature
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

        if (
            !InteropTypeMapper.TryMapType(property.Type, context, out InteropType type)
            || !InteropTypeMapper.CanBeProperty(type.Type)
        )
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
            propertyContract = default!;
            return false;
        }

        NormalizedPropertyAccessor? getter = exposeGetter ? new NormalizedPropertyAccessor(type) : null;
        NormalizedPropertyAccessor? setter = exposeSetter ? new NormalizedPropertyAccessor(type) : null;
        propertyContract = new NormalizedPropertyContract(getter, setter);
        return true;
    }

    /// <summary> Reads the signature of a method that generated code calls by its name. </summary>
    internal static bool TryReadSignature(
        IMethodSymbol method,
        LuauApiSymbols context,
        Location location,
        List<Diagnostic> diagnostics,
        out InteropSignature signature
    )
    {
        signature = default;
        if (method.IsGenericMethod)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                    location,
                    $"method '{method.Name}' is generic, and a script has no type to call it with"
                )
            );
            return false;
        }

        if (SignatureReader.TryRead(method, method.IsAsync, [], context, out signature) is not { } error)
            return true;

        diagnostics.Add(error.ToDiagnostic(location));
        return false;
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
        // An init accessor only runs while the object is created, so a script cannot use it.
        bool hasSetter = property.SetMethod is { IsInitOnly: false };
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
}
