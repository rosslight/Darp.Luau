using Darp.Luau.Generator.Helpers;
using Microsoft.CodeAnalysis;

namespace Darp.Luau.Generator.GeneratedExports;

internal static class ExportDiscovery
{
    public static DiscoveredExportType? DiscoverType(
        INamedTypeSymbol type,
        LuauExportedTypeKind expectedKind,
        LuauApiSymbols context,
        List<Diagnostic> diagnostics
    )
    {
        AttributeData? moduleAttribute = context.GetModuleAttribute(type);
        AttributeData? userdataAttribute = context.GetUserdataAttribute(type);
        if (moduleAttribute is null && userdataAttribute is null)
            return null;

        if (moduleAttribute is not null && userdataAttribute is not null)
        {
            if (expectedKind == LuauExportedTypeKind.Module)
            {
                diagnostics.Add(
                    Diagnostic.Create(
                        DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                        SymbolExtensions.GetAttributeLocation(moduleAttribute, type),
                        $"type '{type.Name}' cannot be both a [LuauModule] and a [LuauUserdata]"
                    )
                );
            }
            return null;
        }

        AttributeData? attribute = expectedKind == LuauExportedTypeKind.Module ? moduleAttribute : userdataAttribute;
        if (attribute is null)
            return null;

        var members = new List<DiscoveredExportMember>();
        var metamethods = new List<DiscoveredExportMetamethod>();
        foreach (ISymbol member in type.GetMembers())
        {
            if (member.IsImplicitlyDeclared)
                continue;

            if (member is IMethodSymbol metamethod && context.GetMetamethodAttribute(metamethod) is { } attributeData)
            {
                metamethods.Add(
                    new DiscoveredExportMetamethod(
                        metamethod,
                        attributeData,
                        new SourceOrigin(member.Name, SymbolExtensions.GetAttributeLocation(attributeData, member))
                    )
                );
            }

            AttributeData? memberAttribute = context.GetMemberAttribute(member);
            if (memberAttribute is null)
                continue;

            SourceOrigin origin = new(member.Name, SymbolExtensions.GetAttributeLocation(memberAttribute, member));
            switch (member)
            {
                case IPropertySymbol property:
                    members.Add(new DiscoveredExportProperty(property, memberAttribute, origin));
                    break;
                case IMethodSymbol method:
                    members.Add(new DiscoveredExportMethod(method, memberAttribute, origin));
                    break;
            }
        }

        return new DiscoveredExportType(
            type,
            expectedKind,
            attribute,
            new SourceOrigin(
                type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                SymbolExtensions.GetAttributeLocation(attribute, type)
            ),
            members.ToImmutableEquatableArray(),
            metamethods.ToImmutableEquatableArray()
        );
    }
}
