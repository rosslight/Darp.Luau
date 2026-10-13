using System.Collections.Immutable;
using Darp.Luau.Generator.CreateFunction;
using Darp.Luau.Generator.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Darp.Luau.Generator;

/// <summary> An analyzer to catch invalid usage of CreateFunction{TDelegate}(TDelegate) </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CreateFunctionUsageAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [
            DiagnosticDescriptors.DirectInvocationRequiredDescriptor,
            DiagnosticDescriptors.UnsupportedTypeDescriptor,
            DiagnosticDescriptors.InvalidDelegateTypeDescriptor,
            DiagnosticDescriptors.InterceptableLocationUnavailableDescriptor,
            DiagnosticDescriptors.UnsupportedReturnTupleDescriptor,
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
            LuauApiSymbols? apiSymbols = LuauApiSymbols.Create(compilationContext.Compilation);
            if (apiSymbols is null)
                return;

            compilationContext.RegisterOperationAction(
                context => AnalyzeMethodReference(context, apiSymbols),
                OperationKind.MethodReference
            );
            compilationContext.RegisterOperationAction(
                context => AnalyzeInvocation(context, apiSymbols),
                OperationKind.Invocation
            );
        });
    }

    private static void AnalyzeMethodReference(OperationAnalysisContext context, LuauApiSymbols apiSymbols)
    {
        var operation = (IMethodReferenceOperation)context.Operation;
        if (!apiSymbols.IsCreateFunctionMethod(operation.Method))
            return;

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.DirectInvocationRequiredDescriptor,
            operation.Syntax.GetLocation(),
            "converting it to a delegate would hit the runtime stub"
        );
        context.ReportDiagnostic(diagnostic);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, LuauApiSymbols apiSymbols)
    {
        var operation = (IInvocationOperation)context.Operation;
        if (!apiSymbols.IsCreateFunctionMethod(operation.TargetMethod))
            return;

        CreateFunctionAnalysisResult analysis = CreateFunctionSignatureAnalyzer.Analyze(operation, apiSymbols);
        foreach (Diagnostic diagnostic in analysis.Diagnostics)
            context.ReportDiagnostic(diagnostic);
    }
}
