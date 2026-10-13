using System.Collections.Immutable;
using Darp.Luau.Generator.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Darp.Luau.Generator.GeneratedExports;

/// <summary>
/// Reads a type marked with <c>[LuauModule]</c> or <c>[LuauUserdata]</c> into what the generator emits for it. The
/// generator takes the result and the analyzer the diagnostics.
/// </summary>
internal static class ExportAnalyzer
{
    /// <summary> Reads a type marked with <c>[LuauModule]</c>. </summary>
    /// <returns>The module, or null when no source can be generated for the type. The diagnostics say why.</returns>
    public static GeneratedModuleIr? AnalyzeModule(
        INamedTypeSymbol type,
        AttributeData attribute,
        LuauApiSymbols context,
        List<Diagnostic> diagnostics
    )
    {
        Location location = SymbolExtensions.GetAttributeLocation(attribute, type);
        if (context.GetUserdataAttribute(type) is not null)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                    location,
                    $"type '{type.Name}' cannot be both a [LuauModule] and a [LuauUserdata]"
                )
            );
            return null;
        }

        const LuauExportedTypeKind kind = LuauExportedTypeKind.Module;
        string? moduleName = ExportValidator.ValidateType(type, kind, attribute, location, context, diagnostics);
        List<AnalyzedExportMember> members = AnalyzeMembers(type, kind, context, diagnostics);
        MetamethodAnalyzer.ReportMetamethodsWithoutUserdataType(type, context, diagnostics);
        ExportValidator.ReportDuplicateNames(type, kind, members, diagnostics);
        GeneratedModuleExportNodeIr root = ExportTreeBuilder.BuildModuleTree(members, diagnostics);
        return moduleName is null ? null : new GeneratedModuleIr(ReadType(type, kind), moduleName, root);
    }

    /// <summary> Reads a type marked with <c>[LuauUserdata]</c>. </summary>
    /// <returns>The userdata type, or null when no source can be generated for it. The diagnostics say why.</returns>
    public static GeneratedUserdataIr? AnalyzeUserdata(
        INamedTypeSymbol type,
        AttributeData attribute,
        LuauApiSymbols context,
        List<Diagnostic> diagnostics
    )
    {
        // A type that is both is reported once, as a module.
        if (context.GetModuleAttribute(type) is not null)
            return null;

        const LuauExportedTypeKind kind = LuauExportedTypeKind.Userdata;
        Location location = SymbolExtensions.GetAttributeLocation(attribute, type);
        string? typeName = ExportValidator.ValidateType(type, kind, attribute, location, context, diagnostics);
        List<AnalyzedExportMember> members = AnalyzeMembers(type, kind, context, diagnostics);
        ImmutableEquatableArray<GeneratedMetamethodIr> metamethods = MetamethodAnalyzer.Analyze(
            type,
            context,
            diagnostics
        );
        ExportValidator.ReportDuplicateNames(type, kind, members, diagnostics);
        return typeName is null
            ? null
            : new GeneratedUserdataIr(
                ReadType(type, kind),
                typeName,
                members.Select(static x => x.Member).ToImmutableEquatableArray(),
                metamethods
            );
    }

    private static List<AnalyzedExportMember> AnalyzeMembers(
        INamedTypeSymbol type,
        LuauExportedTypeKind kind,
        LuauApiSymbols context,
        List<Diagnostic> diagnostics
    )
    {
        var members = new List<AnalyzedExportMember>();
        foreach (ISymbol member in type.GetMembers())
        {
            if (member.IsImplicitlyDeclared || context.GetMemberAttribute(member) is not { } attribute)
                continue;

            Location location = SymbolExtensions.GetAttributeLocation(attribute, member);
            AnalyzedExportMember? analyzed = member switch
            {
                IPropertySymbol property => AnalyzeProperty(property, attribute, kind, context, location, diagnostics),
                IMethodSymbol method => AnalyzeMethod(method, attribute, kind, context, location, diagnostics),
                _ => null,
            };
            if (analyzed is not null)
                members.Add(analyzed);
        }

        return members;
    }

    private static AnalyzedExportMember? AnalyzeProperty(
        IPropertySymbol property,
        AttributeData attribute,
        LuauExportedTypeKind kind,
        LuauApiSymbols context,
        Location location,
        List<Diagnostic> diagnostics
    )
    {
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

        if (kind == LuauExportedTypeKind.Module && !property.IsStatic)
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

        string? exportedName = AttributeReader.GetStringConstructorArgument(attribute);
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

        if (!TryGetPropertyAccess(attribute, location, diagnostics, out LuauExportPropertyAccess requestedAccess))
            return null;

        if (
            !TryResolvePropertyAccess(
                property,
                requestedAccess,
                out bool hasGetter,
                out bool hasSetter,
                out string? reason
            )
        )
        {
            diagnostics.Add(
                Diagnostic.Create(DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor, location, reason)
            );
            return null;
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
                    kind == LuauExportedTypeKind.Module ? "module property" : "userdata property",
                    property.Name
                )
            );
            return null;
        }

        if (kind == LuauExportedTypeKind.Module && hasSetter)
        {
            diagnostics.Add(
                Diagnostic.Create(DiagnosticDescriptors.ModulePropertyMustBeReadOnlyDescriptor, location, property.Name)
            );
            return null;
        }

        // The static side of a userdata type holds the value the property had when a state first asked for it.
        if (kind == LuauExportedTypeKind.Userdata && property.IsStatic && (!hasGetter || hasSetter))
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
                kind,
                exportedName,
                property.Name,
                location,
                diagnostics,
                out ImmutableArray<string> pathSegments
            )
        )
        {
            return null;
        }

        string name = EmitterHelper.EscapeIdentifier(property.Name);
        return new AnalyzedExportMember(
            new GeneratedExportPropertyIr(
                property.IsStatic,
                // A static property is read by its qualified name: a bare name such as 'module' or 'state' would
                // bind to a local of the generated method instead.
                property.IsStatic
                    ? $"{property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{name}"
                    : name,
                exportedName,
                type,
                hasGetter,
                hasSetter
            ),
            pathSegments,
            SymbolExtensions.GetSymbolLocation(property)
        );
    }

    private static AnalyzedExportMember? AnalyzeMethod(
        IMethodSymbol method,
        AttributeData attribute,
        LuauExportedTypeKind kind,
        LuauApiSymbols context,
        Location location,
        List<Diagnostic> diagnostics
    )
    {
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

        string? exportedName = AttributeReader.GetStringConstructorArgument(attribute);
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
                kind,
                exportedName,
                method.Name,
                location,
                diagnostics,
                out ImmutableArray<string> pathSegments
            )
        )
        {
            return null;
        }

        if (!TryReadSignature(method, context, location, diagnostics, out InteropSignature signature))
            return null;

        return new AnalyzedExportMember(
            new GeneratedExportMethodIr(
                method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                method.IsStatic,
                EmitterHelper.EscapeIdentifier(method.Name),
                exportedName,
                signature
            ),
            pathSegments,
            SymbolExtensions.GetSymbolLocation(method)
        );
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
        LuauExportedTypeKind kind,
        string exportedName,
        string memberName,
        Location location,
        List<Diagnostic> diagnostics,
        out ImmutableArray<string> pathSegments
    ) =>
        kind == LuauExportedTypeKind.Module
            ? ExportPathParser.TryParseModulePath(exportedName, location, diagnostics, out pathSegments)
            : ExportPathParser.TryParseUserdataPath(exportedName, memberName, location, diagnostics, out pathSegments);

    private static GeneratedExportTypeIr ReadType(INamedTypeSymbol type, LuauExportedTypeKind kind) =>
        new(
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString(),
            GetTypeDeclaration(type),
            type.IsStatic,
            GetHintName(type, kind)
        );

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
