using System.Text;
using Darp.Luau.Generator.GeneratedExports;
using Darp.Luau.Generator.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Darp.Luau.Generator;

/// <summary>
/// Reports diagnostics and emits runtime registration code for generator-owned Luau modules and userdata.
/// </summary>
[Generator]
public sealed class GeneratedExportsGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<GeneratedModuleIr> moduleModels = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                "Darp.Luau.LuauModuleAttribute",
                static (node, _) => node is TypeDeclarationSyntax,
                static (ctx, _) => Analyze(ctx, ExportAnalyzer.AnalyzeModule)
            )
            .Where(static model => model is not null)
            .Select(static (model, _) => model!)
            .WithTrackingName("GeneratedExportsModuleModels");

        IncrementalValuesProvider<GeneratedUserdataIr> userdataModels = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                "Darp.Luau.LuauUserdataAttribute",
                static (node, _) => node is TypeDeclarationSyntax,
                static (ctx, _) => Analyze(ctx, ExportAnalyzer.AnalyzeUserdata)
            )
            .Where(static model => model is not null)
            .Select(static (model, _) => model!)
            .WithTrackingName("GeneratedExportsUserdataModels");

        context.RegisterSourceOutput(
            moduleModels,
            static (spc, model) => AddSource(spc, model.Type, ModuleEmitter.Emit(model))
        );
        context.RegisterSourceOutput(
            userdataModels,
            static (spc, model) => AddSource(spc, model.Type, UserdataEmitter.Emit(model))
        );
    }

    /// <summary> Reads the type an attribute is on. The diagnostics are the analyzer's to report. </summary>
    private static T? Analyze<T>(
        GeneratorAttributeSyntaxContext context,
        Func<INamedTypeSymbol, AttributeData, LuauApiSymbols, List<Diagnostic>, T?> analyze
    )
        where T : class
    {
        if (
            context.TargetSymbol is not INamedTypeSymbol type
            || LuauApiSymbols.Create(context.SemanticModel.Compilation) is not { } apiSymbols
        )
        {
            return null;
        }

        return analyze(type, context.Attributes[0], apiSymbols, []);
    }

    private static void AddSource(SourceProductionContext spc, GeneratedExportTypeIr type, string source) =>
        spc.AddSource(type.HintName, SourceText.From(source, Encoding.UTF8, SourceHashAlgorithm.Sha256));
}
