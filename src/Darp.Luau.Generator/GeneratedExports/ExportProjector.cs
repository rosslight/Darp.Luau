using Darp.Luau.Generator.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Darp.Luau.Generator.GeneratedExports;

internal static class ExportProjector
{
    public static GeneratedExportSurfaceIr Project(ValidatedExportType validatedType)
    {
        return new GeneratedExportSurfaceIr(
            validatedType.Type.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            validatedType.Type.Symbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : validatedType.Type.Symbol.ContainingNamespace.ToDisplayString(),
            GetTypeDeclaration(validatedType.Type.Symbol),
            validatedType.Type.Symbol.IsStatic,
            GetHintName(validatedType.Type.Symbol, validatedType.Type.Kind),
            validatedType.Type.Kind,
            validatedType.Type.ModuleName,
            validatedType.Type.UserdataTypeName,
            validatedType.Type.Members.Select(ProjectMember).ToImmutableEquatableArray(),
            validatedType.Type.Metamethods,
            validatedType.ModuleRoot is null ? null : ProjectNode(validatedType.ModuleRoot)
        );
    }

    private static GeneratedExportMemberIr ProjectMember(NormalizedExportMember member)
    {
        return member switch
        {
            NormalizedExportPropertyMember property => new GeneratedExportPropertyIr(
                // A static property is read by its qualified name: a bare name such as 'module' or 'state' would
                // bind to a local of the generated method instead.
                property.PropertySymbol.IsStatic
                    ? $"{property.PropertySymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{EscapeIdentifier(property.ManagedName)}"
                    : EscapeIdentifier(property.ManagedName),
                property.LuauName,
                property.PathSegments,
                property.Property.Getter is null ? null : new GeneratedExportAccessorIr(property.Property.Getter.Type),
                property.Property.Setter is null ? null : new GeneratedExportAccessorIr(property.Property.Setter.Type)
            ),
            NormalizedExportMethodMember method => new GeneratedExportMethodIr(
                method.MethodSymbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                method.MethodSymbol.IsStatic,
                EscapeIdentifier(method.ManagedName),
                method.LuauName,
                method.PathSegments,
                method.Signature
            ),
            _ => throw new InvalidOperationException($"Unsupported normalized member type '{member.GetType().Name}'"),
        };
    }

    /// <summary> Writes a member name the way source code refers to it: <c>@return</c> for a member named <c>return</c>. </summary>
    public static string EscapeIdentifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) is SyntaxKind.None ? name : $"@{name}";

    private static GeneratedModuleExportNodeIr ProjectNode(ValidatedModuleExportNode node)
    {
        return new GeneratedModuleExportNodeIr(
            node.Name,
            node.Member is null ? null : ProjectMember(node.Member),
            node.Children.Select(ProjectNode).ToImmutableEquatableArray()
        );
    }

    private static string GetTypeDeclaration(INamedTypeSymbol type)
    {
        TypeDeclarationSyntax syntax = type
            .DeclaringSyntaxReferences.Select(static x => x.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .First();

        string modifiers = string.Join(
            " ",
            syntax
                .Modifiers.Where(static x =>
                    x.IsKind(SyntaxKind.PublicKeyword)
                    || x.IsKind(SyntaxKind.InternalKeyword)
                    || x.IsKind(SyntaxKind.PrivateKeyword)
                    || x.IsKind(SyntaxKind.ProtectedKeyword)
                    || x.IsKind(SyntaxKind.StaticKeyword)
                    || x.IsKind(SyntaxKind.AbstractKeyword)
                    || x.IsKind(SyntaxKind.SealedKeyword)
                    || x.IsKind(SyntaxKind.PartialKeyword)
                )
                .Select(static x => x.Text)
        );

        return syntax switch
        {
            ClassDeclarationSyntax classDeclaration => $"{modifiers} class {classDeclaration.Identifier.Text}",
            StructDeclarationSyntax structDeclaration => $"{modifiers} struct {structDeclaration.Identifier.Text}",
            RecordDeclarationSyntax recordDeclaration =>
                $"{modifiers} record {recordDeclaration.ClassOrStructKeyword.Text} {recordDeclaration.Identifier.Text}",
            _ => throw new InvalidOperationException(
                $"Unsupported generated export type declaration '{syntax.Kind()}'."
            ),
        };
    }

    private static string GetHintName(INamedTypeSymbol type, LuauExportedTypeKind kind)
    {
        string name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        const string globalPrefix = "global::";
        if (name.StartsWith(globalPrefix, StringComparison.Ordinal))
            name = name[globalPrefix.Length..];
        // A type or namespace named like a keyword is displayed as '@class', and a hint name must not contain '@'.
        name = name.Replace("@", string.Empty);

        return name + (kind == LuauExportedTypeKind.Module ? ".LuauModule.g.cs" : ".LuauUserdata.g.cs");
    }
}
