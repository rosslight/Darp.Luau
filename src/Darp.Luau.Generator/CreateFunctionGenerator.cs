using System.Text;
using Darp.Luau.Generator.CreateFunction;
using Darp.Luau.Generator.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace Darp.Luau.Generator;

/// <summary> Interceptor generator for LuauState.CreateFunction. </summary>
[Generator]
public sealed class CreateFunctionGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<CreateFunctionModel> models = context
            .SyntaxProvider.CreateSyntaxProvider(
                predicate: CreateFunctionDiscovery.IsCandidate,
                transform: AnalyzeCandidate
            )
            .Where(static model => model is not null)
            .Select(static (model, _) => model!)
            .WithTrackingName("CreateFunctionModels");

        context.RegisterSourceOutput(models, Emit);
    }

    private static CreateFunctionModel? AnalyzeCandidate(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken
    )
    {
        if (LuauApiSymbols.Create(context.SemanticModel.Compilation) is not { } apiSymbols)
            return null;

        IInvocationOperation? invocation = CreateFunctionDiscovery.GetMatchingInvocation(
            context,
            apiSymbols,
            cancellationToken
        );
        return invocation is null ? null : CreateFunctionSignatureAnalyzer.Analyze(invocation, apiSymbols).Model;
    }

    private static void Emit(SourceProductionContext spc, CreateFunctionModel model)
    {
        bool emitsSource = CreateFunctionEmitter.TryEmit(model, out string? source);
        if (!emitsSource)
            return;

        var sourceText = SourceText.From(source ?? string.Empty, Encoding.UTF8, SourceHashAlgorithm.Sha256);
        spc.AddSource(CreateFunctionEmitter.GetHintName(model), sourceText);
    }
}
