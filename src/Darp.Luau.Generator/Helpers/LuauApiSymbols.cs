using Microsoft.CodeAnalysis;

namespace Darp.Luau.Generator.Helpers;

internal sealed class LuauApiSymbols
{
    private readonly INamedTypeSymbol _delegateTypeSymbol;

    private LuauApiSymbols(
        INamedTypeSymbol luauStateSymbol,
        INamedTypeSymbol delegateTypeSymbol,
        INamedTypeSymbol moduleAttributeSymbol,
        INamedTypeSymbol userdataAttributeSymbol,
        INamedTypeSymbol memberAttributeSymbol,
        INamedTypeSymbol metamethodAttributeSymbol,
        INamedTypeSymbol luauUserdataInterfaceSymbol
    )
    {
        MetamethodAttributeSymbol = metamethodAttributeSymbol;
        LuauStateSymbol = luauStateSymbol;
        _delegateTypeSymbol = delegateTypeSymbol;
        ModuleAttributeSymbol = moduleAttributeSymbol;
        UserdataAttributeSymbol = userdataAttributeSymbol;
        MemberAttributeSymbol = memberAttributeSymbol;
        LuauUserdataInterfaceSymbol = luauUserdataInterfaceSymbol;
    }

    public INamedTypeSymbol LuauStateSymbol { get; }

    public INamedTypeSymbol ModuleAttributeSymbol { get; }

    public INamedTypeSymbol UserdataAttributeSymbol { get; }

    public INamedTypeSymbol MemberAttributeSymbol { get; }

    public INamedTypeSymbol MetamethodAttributeSymbol { get; }

    public INamedTypeSymbol LuauUserdataInterfaceSymbol { get; }

    public static LuauApiSymbols? Create(Compilation compilation)
    {
        INamedTypeSymbol? luauStateSymbol = compilation.GetTypeByMetadataName("Darp.Luau.LuauState");
        INamedTypeSymbol? delegateTypeSymbol = compilation.GetTypeByMetadataName("System.Delegate");
        INamedTypeSymbol? moduleAttributeSymbol = compilation.GetTypeByMetadataName("Darp.Luau.LuauModuleAttribute");
        INamedTypeSymbol? userdataAttributeSymbol = compilation.GetTypeByMetadataName(
            "Darp.Luau.LuauUserdataAttribute"
        );
        INamedTypeSymbol? memberAttributeSymbol = compilation.GetTypeByMetadataName("Darp.Luau.LuauMemberAttribute");
        INamedTypeSymbol? metamethodAttributeSymbol = compilation.GetTypeByMetadataName(
            "Darp.Luau.LuauMetamethodAttribute"
        );
        INamedTypeSymbol? luauUserdataInterfaceSymbol = compilation.GetTypeByMetadataName("Darp.Luau.ILuauUserdata`1");
        if (
            luauStateSymbol is null
            || delegateTypeSymbol is null
            || moduleAttributeSymbol is null
            || userdataAttributeSymbol is null
            || memberAttributeSymbol is null
            || metamethodAttributeSymbol is null
            || luauUserdataInterfaceSymbol is null
        )
        {
            return null;
        }

        return new LuauApiSymbols(
            luauStateSymbol,
            delegateTypeSymbol,
            moduleAttributeSymbol,
            userdataAttributeSymbol,
            memberAttributeSymbol,
            metamethodAttributeSymbol,
            luauUserdataInterfaceSymbol
        );
    }

    public bool IsCreateFunctionMethod(IMethodSymbol method)
    {
        if (!SymbolEqualityComparer.Default.Equals(method.ContainingType, LuauStateSymbol))
            return false;

        return method.OriginalDefinition
                is {
                    Name: "CreateFunction",
                    Parameters: [{ Type: ITypeParameterSymbol { ConstraintTypes.Length: 1 } typeSymbol }],
                }
            && SymbolEqualityComparer.Default.Equals(typeSymbol.ConstraintTypes[0], _delegateTypeSymbol);
    }

    public AttributeData? GetModuleAttribute(ISymbol symbol) =>
        AttributeReader.GetAttribute(symbol, ModuleAttributeSymbol);

    public AttributeData? GetUserdataAttribute(ISymbol symbol) =>
        AttributeReader.GetAttribute(symbol, UserdataAttributeSymbol);

    public AttributeData? GetMemberAttribute(ISymbol symbol) =>
        AttributeReader.GetAttribute(symbol, MemberAttributeSymbol);

    public AttributeData? GetMetamethodAttribute(ISymbol symbol) =>
        AttributeReader.GetAttribute(symbol, MetamethodAttributeSymbol);

    /// <summary> Gets the name of the <c>LuauMetamethod</c> value an attribute names, or null when it names none. </summary>
    public static string? GetMetamethodName(AttributeData attribute)
    {
        if (attribute.ConstructorArguments is not [{ Type: INamedTypeSymbol enumType, Value: { } value }])
            return null;

        return enumType
            .GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(field => field.HasConstantValue && Equals(field.ConstantValue, value))
            ?.Name;
    }

    public bool ImplementsManualUserdataHooks(INamedTypeSymbol type) => type.ImplementsManualUserdataHooks(this);
}
