using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Darp.Luau.Generator.Helpers;

internal static class SymbolExtensions
{
    public static bool IsPartial(this INamedTypeSymbol type)
    {
        return type.DeclaringSyntaxReferences.Length > 0
            && type.DeclaringSyntaxReferences.Select(static x => x.GetSyntax())
                .OfType<TypeDeclarationSyntax>()
                .All(static x => x.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PartialKeyword)));
    }

    public static bool IsFileLocal(this INamedTypeSymbol type)
    {
        return type
            .DeclaringSyntaxReferences.Select(static x => x.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .Any(static x => x.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.FileKeyword)));
    }

    public static bool ImplementsManualUserdataHooks(this INamedTypeSymbol type, LuauApiSymbols apiSymbols)
    {
        return type.GetManualUserdataHookMembers(apiSymbols).Any();
    }

    public static IEnumerable<ISymbol> GetManualUserdataHookMembers(
        this INamedTypeSymbol type,
        LuauApiSymbols apiSymbols
    )
    {
        if (
            !type.AllInterfaces.Any(@interface =>
                SymbolEqualityComparer.Default.Equals(
                    @interface.OriginalDefinition,
                    apiSymbols.LuauUserdataInterfaceSymbol
                )
            )
        )
            yield break;

        foreach (INamedTypeSymbol @interface in type.AllInterfaces)
        {
            if (
                !SymbolEqualityComparer.Default.Equals(
                    @interface.OriginalDefinition,
                    apiSymbols.LuauUserdataInterfaceSymbol
                )
            )
                continue;

            foreach (ISymbol interfaceMember in @interface.GetMembers())
            {
                ISymbol? implementation = type.FindImplementationForInterfaceMember(interfaceMember);
                if (implementation is IMethodSymbol && implementation.HasNonGeneratedDeclaration())
                {
                    yield return implementation;
                }
            }
        }
    }

    public static bool HasNonGeneratedDeclaration(this ISymbol symbol)
    {
        return !symbol.HasGeneratedCodeAttribute();
    }

    public static bool HasGeneratedCodeAttribute(this ISymbol symbol) =>
        symbol.GetAttributes().Any(static attribute => attribute.AttributeClass.IsGeneratedCodeAttribute());

    public static Location GetAttributeLocation(AttributeData attribute, ISymbol fallbackSymbol)
    {
        return attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? GetSymbolLocation(fallbackSymbol);
    }

    public static Location GetSymbolLocation(ISymbol symbol)
    {
        return symbol.Locations.FirstOrDefault() ?? Location.None;
    }

    private static bool IsGeneratedCodeAttribute(this INamedTypeSymbol? attributeType)
    {
        return attributeType is { Name: "GeneratedCodeAttribute" }
            && attributeType.ContainingNamespace.ToDisplayString() == "System.CodeDom.Compiler";
    }
}
