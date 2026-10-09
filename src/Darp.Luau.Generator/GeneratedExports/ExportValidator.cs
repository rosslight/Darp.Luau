using Darp.Luau.Generator.Helpers;
using Microsoft.CodeAnalysis;

namespace Darp.Luau.Generator.GeneratedExports;

internal static class ExportValidator
{
    public static bool ValidateTypeShape(
        DiscoveredExportType discoveredType,
        LuauApiSymbols context,
        List<Diagnostic> diagnostics
    )
    {
        return ValidateType(discoveredType, context, diagnostics);
    }

    public static ValidatedExportType ValidateMembers(
        DiscoveredExportType discoveredType,
        NormalizedExportType normalizedType,
        List<Diagnostic> diagnostics
    )
    {
        ReportDuplicateNames(discoveredType.Symbol, normalizedType.Kind, normalizedType.Members, diagnostics);

        ValidatedModuleExportNode? moduleRoot =
            normalizedType.Kind == LuauExportedTypeKind.Module
                ? ExportTreeBuilder.BuildModuleTree(normalizedType, diagnostics)
                : null;
        return new ValidatedExportType(normalizedType, moduleRoot);
    }

    private static bool ValidateType(
        DiscoveredExportType discoveredType,
        LuauApiSymbols context,
        List<Diagnostic> diagnostics
    )
    {
        bool hasFatalTypeErrors = false;
        if (!SymbolExtensions.IsPartial(discoveredType.Symbol))
        {
            hasFatalTypeErrors = true;
            diagnostics.Add(
                Diagnostic.Create(
                    discoveredType.Kind == LuauExportedTypeKind.Module
                        ? DiagnosticDescriptors.LuauModuleTypeMustBePartialDescriptor
                        : DiagnosticDescriptors.LuauUserdataTypeMustBePartialDescriptor,
                    discoveredType.Origin.Location
                )
            );
        }

        if (discoveredType.Symbol.IsFileLocal())
        {
            hasFatalTypeErrors = true;
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                    discoveredType.Origin.Location,
                    "file-local generated export types are not supported because generated partial sources are emitted into separate files"
                )
            );
        }

        if (discoveredType.Kind == LuauExportedTypeKind.Module)
        {
            string? moduleName = AttributeReader.GetStringConstructorArgument(discoveredType.Attribute);
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                hasFatalTypeErrors = true;
                diagnostics.Add(
                    Diagnostic.Create(
                        DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                        discoveredType.Origin.Location,
                        "root modules must supply a non-empty name via [LuauModule(\"...\")]"
                    )
                );
            }
            else if (IsReservedModuleName(moduleName!))
            {
                hasFatalTypeErrors = true;
                diagnostics.Add(
                    Diagnostic.Create(
                        DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                        discoveredType.Origin.Location,
                        "module names must be bare require names; './', '../', '/', '\\', '@', and slash-containing names are reserved for script modules"
                    )
                );
            }

            if (discoveredType.Symbol.ContainingType is not null)
            {
                hasFatalTypeErrors = true;
                diagnostics.Add(
                    Diagnostic.Create(
                        DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                        discoveredType.Origin.Location,
                        "nested module types are not supported in v1"
                    )
                );
            }

            if (discoveredType.Symbol.TypeParameters.Length > 0)
            {
                hasFatalTypeErrors = true;
                diagnostics.Add(
                    Diagnostic.Create(
                        DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                        discoveredType.Origin.Location,
                        "generic module types are not supported in v1"
                    )
                );
            }

            hasFatalTypeErrors |= ReportGeneratedModuleMemberNameConflicts(discoveredType.Symbol, diagnostics);

            if (discoveredType.Symbol.TypeKind == TypeKind.Struct)
            {
                hasFatalTypeErrors = true;
                diagnostics.Add(
                    Diagnostic.Create(
                        DiagnosticDescriptors.InstanceModuleStructNotSupportedDescriptor,
                        discoveredType.Origin.Location
                    )
                );
            }

            return hasFatalTypeErrors;
        }

        if (discoveredType.Symbol.TypeKind != TypeKind.Class)
        {
            hasFatalTypeErrors = true;
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                    discoveredType.Origin.Location,
                    "types marked with [LuauUserdata] must be classes"
                )
            );
        }

        if (discoveredType.Symbol.ContainingType is not null)
        {
            hasFatalTypeErrors = true;
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                    discoveredType.Origin.Location,
                    "nested userdata types are not supported in v1"
                )
            );
        }

        if (discoveredType.Symbol.TypeParameters.Length > 0)
        {
            hasFatalTypeErrors = true;
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                    discoveredType.Origin.Location,
                    "generic userdata types are not supported in v1"
                )
            );
        }

        foreach (ISymbol hookMember in discoveredType.Symbol.GetManualUserdataHookMembers(context))
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.GeneratedUserdataManualInteropConflictDescriptor,
                    SymbolExtensions.GetSymbolLocation(hookMember),
                    discoveredType.Symbol.Name
                )
            );
            hasFatalTypeErrors = true;
        }

        return hasFatalTypeErrors;
    }

    private static bool IsReservedModuleName(string name) =>
        name.StartsWith(".", StringComparison.Ordinal)
        || name.StartsWith("/", StringComparison.Ordinal)
        || name.StartsWith("\\", StringComparison.Ordinal)
        || name.StartsWith("@", StringComparison.Ordinal)
        || name.Contains("/", StringComparison.Ordinal)
        || name.Contains("\\", StringComparison.Ordinal);

    private static bool ReportGeneratedModuleMemberNameConflicts(INamedTypeSymbol type, List<Diagnostic> diagnostics)
    {
        bool hasConflicts = false;
        foreach (ISymbol member in type.GetMembers())
        {
            if (member.IsImplicitlyDeclared)
                continue;

            if ((member.Name is "ModuleName" or "OnLoad") && member.HasNonGeneratedDeclaration())
            {
                diagnostics.Add(
                    Diagnostic.Create(
                        DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                        SymbolExtensions.GetSymbolLocation(member),
                        "module types cannot declare members named 'ModuleName' or 'OnLoad' because those names are generated"
                    )
                );
                hasConflicts = true;
            }
        }

        return hasConflicts;
    }

    private static void ReportDuplicateNames(
        INamedTypeSymbol type,
        LuauExportedTypeKind exportedTypeKind,
        ImmutableEquatableArray<NormalizedExportMember> members,
        List<Diagnostic> diagnostics
    )
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (NormalizedExportMember member in members)
        {
            if (!names.Add(member.LuauName))
            {
                diagnostics.Add(
                    Diagnostic.Create(
                        DiagnosticDescriptors.DuplicateLuauMemberNameDescriptor,
                        SymbolExtensions.GetSymbolLocation(member.Symbol),
                        member.LuauName,
                        exportedTypeKind == LuauExportedTypeKind.Module ? "module" : "userdata",
                        type.Name
                    )
                );
            }
        }
    }
}
