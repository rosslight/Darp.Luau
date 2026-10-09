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

    /// <summary> Gets the <c>Register</c> methods a type wrote itself. </summary>
    public static IEnumerable<ISymbol> GetManualUserdataHookMembers(
        this INamedTypeSymbol type,
        LuauApiSymbols apiSymbols
    )
    {
        var registrations = new List<ISymbol>();
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
                    registrations.Add(implementation);
            }
        }

        // A type that declares the method without listing the interface means the same. The generated explicit
        // implementation would take its place without a word.
        foreach (IMethodSymbol method in type.GetMembers("Register").OfType<IMethodSymbol>())
        {
            if (
                method
                    is {
                        IsStatic: true,
                        Parameters: [{ Type: INamedTypeSymbol { Name: "LuauUserdataRegistry", Arity: 1 } registry }],
                    }
                && registry.ContainingNamespace.ToDisplayString() == "Darp.Luau"
                && SymbolEqualityComparer.Default.Equals(registry.TypeArguments[0], type)
                && method.HasNonGeneratedDeclaration()
                && !registrations.Contains(method, SymbolEqualityComparer.Default)
            )
            {
                registrations.Add(method);
            }
        }

        return registrations;
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
