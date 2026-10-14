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
            DiagnosticDescriptors.UnsupportedTypeDescriptor,
            DiagnosticDescriptors.UnsupportedReturnTupleDescriptor,
            DiagnosticDescriptors.LuauModuleTypeMustBePartialDescriptor,
            DiagnosticDescriptors.LuauUserdataTypeMustBePartialDescriptor,
            DiagnosticDescriptors.DuplicateLuauMemberNameDescriptor,
            DiagnosticDescriptors.UnsupportedGeneratedPropertyTypeDescriptor,
            DiagnosticDescriptors.InstanceModuleStructNotSupportedDescriptor,
            DiagnosticDescriptors.LuauExportPathConflictDescriptor,
            DiagnosticDescriptors.InvalidLuauExportPathDescriptor,
            DiagnosticDescriptors.ModulePropertyMustBeReadOnlyDescriptor,
            DiagnosticDescriptors.GeneratedUserdataManualInteropConflictDescriptor,
            DiagnosticDescriptors.InvalidGeneratedExportShapeDescriptor,
            DiagnosticDescriptors.LuauExportPathSegmentRequiresBracketAccessDescriptor,
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
        var diagnostics = new List<Diagnostic>();
        if (apiSymbols.GetModuleAttribute(type) is { } moduleAttribute)
            ExportAnalyzer.AnalyzeModule(type, moduleAttribute, apiSymbols, diagnostics);
        else if (apiSymbols.GetUserdataAttribute(type) is { } userdataAttribute)
            ExportAnalyzer.AnalyzeUserdata(type, userdataAttribute, apiSymbols, diagnostics);
        else
            MetamethodAnalyzer.ReportMetamethodsWithoutUserdataType(type, apiSymbols, diagnostics);

        foreach (Diagnostic diagnostic in diagnostics)
            context.ReportDiagnostic(diagnostic);
    }
}
