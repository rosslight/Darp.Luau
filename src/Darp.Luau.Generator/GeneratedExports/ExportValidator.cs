using Darp.Luau.Generator.Helpers;
using Microsoft.CodeAnalysis;

namespace Darp.Luau.Generator.GeneratedExports;

internal static class ExportValidator
{
    /// <summary> Checks that generated code can add to <paramref name="type"/>. </summary>
    /// <returns>
    /// The name the attribute gives the module or the userdata type, or null when no source can be generated for
    /// the type.
    /// </returns>
    public static string? ValidateType(
        INamedTypeSymbol type,
        LuauExportedTypeKind kind,
        AttributeData attribute,
        Location location,
        LuauApiSymbols context,
        List<Diagnostic> diagnostics
    )
    {
        bool hasFatalTypeErrors = false;
        if (!SymbolExtensions.IsPartial(type))
        {
            hasFatalTypeErrors = true;
            diagnostics.Add(
                Diagnostic.Create(
                    kind == LuauExportedTypeKind.Module
                        ? DiagnosticDescriptors.LuauModuleTypeMustBePartialDescriptor
                        : DiagnosticDescriptors.LuauUserdataTypeMustBePartialDescriptor,
                    location
                )
            );
        }

        if (type.IsFileLocal())
        {
            hasFatalTypeErrors = true;
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                    location,
                    "file-local generated export types are not supported because generated partial sources are emitted into separate files"
                )
            );
        }

        if (kind == LuauExportedTypeKind.Module)
        {
            string? moduleName = AttributeReader.GetStringConstructorArgument(attribute);
            if (moduleName is null || string.IsNullOrWhiteSpace(moduleName))
            {
                hasFatalTypeErrors = true;
                diagnostics.Add(
                    Diagnostic.Create(
                        DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                        location,
                        "root modules must supply a non-empty name via [LuauModule(\"...\")]"
                    )
                );
            }
            else if (IsReservedModuleName(moduleName))
            {
                hasFatalTypeErrors = true;
                diagnostics.Add(
                    Diagnostic.Create(
                        DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                        location,
                        "module names must be bare require names; './', '../', '/', '\\', '@', and slash-containing names are reserved for script modules"
                    )
                );
            }

            if (type.ContainingType is not null)
            {
                hasFatalTypeErrors = true;
                diagnostics.Add(
                    Diagnostic.Create(
                        DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                        location,
                        "nested module types are not supported in v1"
                    )
                );
            }

            if (type.TypeParameters.Length > 0)
            {
                hasFatalTypeErrors = true;
                diagnostics.Add(
                    Diagnostic.Create(
                        DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                        location,
                        "generic module types are not supported in v1"
                    )
                );
            }

            hasFatalTypeErrors |= ReportGeneratedModuleMemberNameConflicts(type, diagnostics);

            if (type.TypeKind == TypeKind.Struct)
            {
                hasFatalTypeErrors = true;
                diagnostics.Add(
                    Diagnostic.Create(DiagnosticDescriptors.InstanceModuleStructNotSupportedDescriptor, location)
                );
            }

            return hasFatalTypeErrors ? null : moduleName;
        }

        if (type.TypeKind != TypeKind.Class)
        {
            hasFatalTypeErrors = true;
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                    location,
                    "types marked with [LuauUserdata] must be classes"
                )
            );
        }

        // Scripts see the name as typeof(value), and a declaration of the type for an editor needs an identifier.
        string? typeName = AttributeReader.GetStringConstructorArgument(attribute);
        if (typeName is null || !ExportPathParser.IsLuauDotPathIdentifier(typeName) || IsLuauTypeName(typeName))
        {
            hasFatalTypeErrors = true;
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                    location,
                    $"userdata type name '{typeName}' must be a Luau identifier that is not the name of a built-in Luau type"
                )
            );
        }

        if (type.ContainingType is not null)
        {
            hasFatalTypeErrors = true;
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                    location,
                    "nested userdata types are not supported in v1"
                )
            );
        }

        if (type.TypeParameters.Length > 0)
        {
            hasFatalTypeErrors = true;
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                    location,
                    "generic userdata types are not supported in v1"
                )
            );
        }

        foreach (ISymbol hookMember in type.GetManualUserdataHookMembers(context))
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.GeneratedUserdataManualInteropConflictDescriptor,
                    SymbolExtensions.GetSymbolLocation(hookMember),
                    type.Name
                )
            );
            hasFatalTypeErrors = true;
        }

        return hasFatalTypeErrors ? null : typeName;
    }

    private static bool IsLuauTypeName(string name) =>
        name
            is "boolean"
                or "number"
                or "string"
                or "table"
                or "thread"
                or "userdata"
                or "vector"
                or "buffer"
                or "any"
                or "unknown"
                or "never";

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

    public static void ReportDuplicateNames(
        INamedTypeSymbol type,
        LuauExportedTypeKind exportedTypeKind,
        List<AnalyzedExportMember> members,
        List<Diagnostic> diagnostics
    )
    {
        // The instance and the static side of a userdata type are different tables, so a name can be on both.
        var names = new HashSet<(bool IsStatic, string Name)>();
        foreach (AnalyzedExportMember member in members)
        {
            bool isStatic = exportedTypeKind == LuauExportedTypeKind.Userdata && member.Member.IsStatic;
            if (!names.Add((isStatic, member.Member.LuauName)))
            {
                diagnostics.Add(
                    Diagnostic.Create(
                        DiagnosticDescriptors.DuplicateLuauMemberNameDescriptor,
                        member.Location,
                        member.Member.LuauName,
                        exportedTypeKind == LuauExportedTypeKind.Module ? "module" : "userdata",
                        type.Name
                    )
                );
            }
        }
    }
}
