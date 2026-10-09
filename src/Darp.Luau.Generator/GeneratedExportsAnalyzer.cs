using System.Collections.Immutable;
using Darp.Luau.Generator.GeneratedExports;
using Darp.Luau.Generator.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Darp.Luau.Generator;

/// <summary> Reports diagnostics for generated Luau modules and userdata. </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GeneratedExportsAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [
            DiagnosticDescriptors.LuauModuleTypeMustBePartialDescriptor,
            DiagnosticDescriptors.LuauUserdataTypeMustBePartialDescriptor,
            DiagnosticDescriptors.DuplicateLuauMemberNameDescriptor,
            DiagnosticDescriptors.UnsupportedGeneratedPropertyTypeDescriptor,
            DiagnosticDescriptors.InstanceModuleStructNotSupportedDescriptor,
            DiagnosticDescriptors.UnsupportedGeneratedFunctionShapeDescriptor,
            DiagnosticDescriptors.LuauExportPathConflictDescriptor,
            DiagnosticDescriptors.InvalidLuauExportPathDescriptor,
            DiagnosticDescriptors.ModulePropertyMustBeReadOnlyDescriptor,
            DiagnosticDescriptors.GeneratedUserdataManualInteropConflictDescriptor,
            DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
            DiagnosticDescriptors.LuauExportPathSegmentRequiresBracketAccessDescriptor,
            DiagnosticDescriptors.UnreachableMetamethodOverloadDescriptor,
        ];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static compilationContext =>
        {
            var apiSymbols = LuauApiSymbols.Create(compilationContext.Compilation);
            if (apiSymbols is null)
                return;

            compilationContext.RegisterSymbolAction(
                context => AnalyzeNamedType(context, apiSymbols),
                SymbolKind.NamedType
            );
        });
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context, LuauApiSymbols apiSymbols)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        AttributeData? moduleAttribute = apiSymbols.GetModuleAttribute(type);
        AttributeData? userdataAttribute = apiSymbols.GetUserdataAttribute(type);
        if (moduleAttribute is null && userdataAttribute is null)
        {
            ReportMetamethodsWithoutUserdataType(context, type, apiSymbols);
            return;
        }

        LuauExportedTypeKind expectedKind = moduleAttribute is not null
            ? LuauExportedTypeKind.Module
            : LuauExportedTypeKind.Userdata;
        GeneratedExportsTypeAnalysis analysis = ExportAnalyzer.AnalyzeType(type, expectedKind, context.Compilation);
        foreach (Diagnostic diagnostic in analysis.Diagnostics)
            context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    /// The generator only looks at types with <c>[LuauUserdata]</c>. On any other type the attribute would do nothing,
    /// for example on a type that registers its members by hand.
    /// </summary>
    private static void ReportMetamethodsWithoutUserdataType(
        SymbolAnalysisContext context,
        INamedTypeSymbol type,
        LuauApiSymbols apiSymbols
    )
    {
        foreach (IMethodSymbol method in type.GetMembers().OfType<IMethodSymbol>())
        {
            AttributeData? attribute = apiSymbols.GetMetamethodAttribute(method);
            if (attribute is null)
                continue;

            context.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
                    SymbolExtensions.GetAttributeLocation(attribute, method),
                    $"method '{method.Name}' is a metamethod, which only a [LuauUserdata] type has"
                )
            );
        }
    }
}
