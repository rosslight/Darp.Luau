using System.Collections.Immutable;
using Darp.Luau.Generator.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Darp.Luau.Generator.CreateFunction;

internal static class CreateFunctionSignatureAnalyzer
{
    public static CreateFunctionAnalysisResult Analyze(IInvocationOperation invocationOperation)
    {
        var diagnostics = new List<Diagnostic>();
        var syntax = (InvocationExpressionSyntax)invocationOperation.Syntax;
        if (invocationOperation.SemanticModel is not { } semanticModel)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InterceptableLocationUnavailableDescriptor,
                    syntax.GetLocation(),
                    "the semantic model was unavailable"
                )
            );
            return new CreateFunctionAnalysisResult(null, diagnostics.ToImmutableArray());
        }

        InterceptableLocation? location = semanticModel.GetInterceptableLocation(syntax);
        LuauApiSymbols? apiSymbols = LuauApiSymbols.Create(semanticModel.Compilation);
        if (location is null)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InterceptableLocationUnavailableDescriptor,
                    syntax.GetLocation(),
                    "the compiler did not provide an interceptable location for this invocation"
                )
            );
            return new CreateFunctionAnalysisResult(null, diagnostics.ToImmutableArray());
        }

        if (!TryExtractSignature(invocationOperation, apiSymbols, out InteropSignature signature, diagnostics))
            return new CreateFunctionAnalysisResult(null, diagnostics.ToImmutableArray());

        return new CreateFunctionAnalysisResult(
            new CreateFunctionModel(new InterceptorLocationData(location.Version, location.Data), signature),
            diagnostics.ToImmutableArray()
        );
    }

    private static bool TryExtractSignature(
        IInvocationOperation invocationOperation,
        LuauApiSymbols? apiSymbols,
        out InteropSignature signature,
        List<Diagnostic> diagnostics
    )
    {
        signature = default;
        ITypeSymbol? delegateType = invocationOperation.Arguments.Select(x => x.Value.Type).FirstOrDefault();

        if (delegateType is not INamedTypeSymbol { DelegateInvokeMethod: { } invokeMethod })
        {
            var syntax = (InvocationExpressionSyntax)invocationOperation.Syntax;
            Location diagnosticLocation = syntax.GetLocation();

            GenericNameSyntax? genericName = syntax.Expression switch
            {
                GenericNameSyntax directGeneric => directGeneric,
                MemberAccessExpressionSyntax { Name: GenericNameSyntax memberGeneric } => memberGeneric,
                _ => null,
            };
            if (genericName?.TypeArgumentList.Arguments.Count > 0)
            {
                diagnosticLocation = genericName.TypeArgumentList.Arguments[0].GetLocation();
            }
            else if (syntax.ArgumentList.Arguments.Count > 0)
            {
                ArgumentSyntax argSyntax = syntax.ArgumentList.Arguments[0];
                diagnosticLocation = argSyntax.Expression.GetLocation();
            }

            string typeDisplayName = delegateType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "null";
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidDelegateTypeDescriptor,
                    diagnosticLocation,
                    typeDisplayName
                )
            );
            return false;
        }

        if (invokeMethod.ReturnsVoid && IsAsyncCallback(invocationOperation))
        {
            // An async void callback reports success at once and runs on unobserved; nothing could await its work.
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.UnsupportedTypeDescriptor,
                    GetReturnLocation(invocationOperation),
                    "void",
                    "the result of an async callback (return Task or ValueTask so the script can wait for the work)"
                )
            );
            return false;
        }

        var parameters = ImmutableArray.CreateBuilder<InteropType>();
        var returnTypes = ImmutableArray.CreateBuilder<InteropType>();
        ImmutableArray<LambdaReturnOverride> returnOverrides = LambdaReturnNullabilityResolver.GetReturnOverrides(
            invocationOperation
        );

        for (int i = 0; i < invokeMethod.Parameters.Length; i++)
        {
            IParameterSymbol parameter = invokeMethod.Parameters[i];
            Location parameterLocation = GetParameterLocation(invocationOperation, i);
            string usageDescription = parameter.Name is { Length: > 0 }
                ? $"parameter '{parameter.Name}'"
                : $"parameter #{i + 1}";

            if (
                !TryMapTypeToInteropType(
                    parameter.Type,
                    parameterLocation,
                    usageDescription,
                    LuauInteropTypeUsage.CreateFunctionParameter,
                    apiSymbols,
                    out InteropType parameterType,
                    diagnostics
                )
            )
            {
                return false;
            }

            if (parameter.NullableAnnotation is NullableAnnotation.Annotated)
                parameterType = parameterType with { IsNullable = true };

            if (parameterType.IsNullable && !InteropTypeMapper.SupportsNullableValue(parameterType.Type))
            {
                diagnostics.Add(
                    Diagnostic.Create(
                        DiagnosticDescriptors.UnsupportedTypeDescriptor,
                        parameterLocation,
                        parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        usageDescription
                    )
                );
                return false;
            }

            parameters.Add(parameterType);
        }

        AwaitableReturnKind awaitable = InteropTypeMapper.GetAwaitableKind(
            invokeMethod.ReturnType,
            out ITypeSymbol? awaitedType
        );
        (ITypeSymbol? returnType, NullableAnnotation returnNullableAnnotation) =
            awaitable is AwaitableReturnKind.None
                ? (invokeMethod.ReturnType, invokeMethod.ReturnNullableAnnotation)
                : (awaitedType, awaitedType?.NullableAnnotation ?? NullableAnnotation.None);
        if (
            returnType is not null
            && !TryExtractReturnTypes(
                invocationOperation,
                apiSymbols,
                returnType,
                returnNullableAnnotation,
                returnOverrides,
                returnTypes,
                diagnostics
            )
        )
        {
            return false;
        }

        signature = new InteropSignature(
            parameters.ToImmutableEquatableArray(),
            returnTypes.ToImmutableEquatableArray(),
            awaitable
        );
        return true;
    }

    /// <summary> Whether the callback is an async lambda or an async method. A delegate held in a variable is not seen. </summary>
    private static bool IsAsyncCallback(IInvocationOperation invocationOperation)
    {
        IOperation? callback = invocationOperation.Arguments.FirstOrDefault()?.Value;
        while (callback is IConversionOperation conversion)
            callback = conversion.Operand;

        return callback
            is IDelegateCreationOperation
            {
                Target: IAnonymousFunctionOperation { Symbol.IsAsync: true }
                    or IMethodReferenceOperation { Method.IsAsync: true }
            };
    }

    private static bool TryMapTypeToInteropType(
        ITypeSymbol type,
        Location diagnosticLocation,
        string usageDescription,
        LuauInteropTypeUsage usage,
        LuauApiSymbols? apiSymbols,
        out InteropType interopType,
        List<Diagnostic> diagnostics
    )
    {
        if (
            !InteropTypeMapper.TryMapType(type, out InteropType mapping)
            && (
                apiSymbols is null
                || !InteropTypeMapper.TryMapGeneratedUserdataType(type, apiSymbols, out mapping)
            )
        )
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.UnsupportedTypeDescriptor,
                    diagnosticLocation,
                    type.ToDisplayString(),
                    usageDescription
                )
            );
            interopType = default;
            return false;
        }

        if (!InteropTypeMapper.SupportsUsage(mapping, usage))
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.UnsupportedTypeDescriptor,
                    diagnosticLocation,
                    type.ToDisplayString(),
                    usageDescription
                )
            );
            interopType = default;
            return false;
        }

        if (mapping.IsNullable && !InteropTypeMapper.SupportsNullableValue(mapping.Type))
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.UnsupportedTypeDescriptor,
                    diagnosticLocation,
                    type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    usageDescription
                )
            );
            interopType = default;
            return false;
        }

        interopType = mapping;
        return true;
    }

    private static bool TryExtractReturnTypes(
        IInvocationOperation invocationOperation,
        LuauApiSymbols? apiSymbols,
        ITypeSymbol returnType,
        NullableAnnotation returnNullableAnnotation,
        ImmutableArray<LambdaReturnOverride> returnOverrides,
        ImmutableArray<InteropType>.Builder returnTypes,
        List<Diagnostic> diagnostics
    )
    {
        if (returnType.SpecialType is SpecialType.System_Void)
            return true;

        Location returnLocation = GetReturnLocation(invocationOperation);
        if (returnType is not INamedTypeSymbol { IsTupleType: true } tupleType)
            return TryExtractSingleReturnType(
                returnType,
                returnLocation,
                "the return value",
                returnTypes,
                diagnostics,
                apiSymbols,
                nullableAnnotationOverride: returnNullableAnnotation,
                nullableOverride: returnOverrides is [var singleOverride] ? singleOverride.IsNullable : null
            );

        if (tupleType.TupleElements.Length > 4)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.UnsupportedReturnTupleDescriptor,
                    returnLocation,
                    tupleType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    "only up to 4 tuple return values are supported"
                )
            );
            return false;
        }

        for (int i = 0; i < tupleType.TupleElements.Length; i++)
        {
            IFieldSymbol tupleElement = tupleType.TupleElements[i];
            if (tupleElement.Type is INamedTypeSymbol { IsTupleType: true } nestedTupleType)
            {
                diagnostics.Add(
                    Diagnostic.Create(
                        DiagnosticDescriptors.UnsupportedReturnTupleDescriptor,
                        returnLocation,
                        tupleType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        $"nested tuple element '{nestedTupleType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}' is not supported"
                    )
                );
                return false;
            }

            string usageDescription = tupleElement.Name is { Length: > 0 }
                ? $"return tuple element '{tupleElement.Name}'"
                : "a return tuple element";
            if (
                !TryExtractSingleReturnType(
                    tupleElement.Type,
                    returnLocation,
                    usageDescription,
                    returnTypes,
                    diagnostics,
                    apiSymbols,
                    tupleElement.NullableAnnotation,
                    tupleElement.Name,
                    returnOverrides.Length > i ? returnOverrides[i].IsNullable : null
                )
            )
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryExtractSingleReturnType(
        ITypeSymbol returnType,
        Location returnLocation,
        string returnUsageDescription,
        ImmutableArray<InteropType>.Builder returnTypes,
        List<Diagnostic> diagnostics,
        LuauApiSymbols? apiSymbols,
        NullableAnnotation? nullableAnnotationOverride = null,
        string? tupleElementName = null,
        bool? nullableOverride = null
    )
    {
        if (
            !TryMapTypeToInteropType(
                returnType,
                returnLocation,
                returnUsageDescription,
                LuauInteropTypeUsage.CreateFunctionReturn,
                apiSymbols,
                out InteropType mappedReturnType,
                diagnostics
            )
        )
        {
            return false;
        }

        bool isReturnNullable = mappedReturnType.IsNullable;
        if (nullableAnnotationOverride is NullableAnnotation.Annotated)
            isReturnNullable = true;

        if (nullableOverride is true)
            isReturnNullable = true;

        if (isReturnNullable && !InteropTypeMapper.SupportsNullableValue(mappedReturnType.Type))
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.UnsupportedTypeDescriptor,
                    returnLocation,
                    returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    returnUsageDescription
                )
            );
            return false;
        }

        returnTypes.Add(mappedReturnType with { IsNullable = isReturnNullable, TupleElementName = tupleElementName });
        return true;
    }

    private static Location GetParameterLocation(IInvocationOperation invocationOperation, int parameterIndex)
    {
        var syntax = (InvocationExpressionSyntax)invocationOperation.Syntax;

        if (syntax.ArgumentList.Arguments.Count == 0)
            return syntax.GetLocation();

        ExpressionSyntax delegateExpression = syntax.ArgumentList.Arguments[0].Expression;
        switch (delegateExpression)
        {
            case ParenthesizedLambdaExpressionSyntax parenthesizedLambda:
                if (parameterIndex < parenthesizedLambda.ParameterList.Parameters.Count)
                {
                    ParameterSyntax parameterSyntax = parenthesizedLambda.ParameterList.Parameters[parameterIndex];
                    return (parameterSyntax.Type ?? (SyntaxNode)parameterSyntax).GetLocation();
                }
                break;
            case SimpleLambdaExpressionSyntax simpleLambda when parameterIndex == 0:
                ParameterSyntax simpleParameterSyntax = simpleLambda.Parameter;
                return (simpleParameterSyntax.Type ?? (SyntaxNode)simpleParameterSyntax).GetLocation();
        }
        return delegateExpression.GetLocation();
    }

    private static Location GetReturnLocation(IInvocationOperation invocationOperation)
    {
        var syntax = (InvocationExpressionSyntax)invocationOperation.Syntax;
        if (syntax.ArgumentList.Arguments.Count == 0)
            return syntax.GetLocation();

        ExpressionSyntax delegateExpression = syntax.ArgumentList.Arguments[0].Expression;
        switch (delegateExpression)
        {
            case ParenthesizedLambdaExpressionSyntax parenthesizedLambda:
                if (parenthesizedLambda.ExpressionBody is { } exprBody)
                    return exprBody.GetLocation();

                if (parenthesizedLambda.Block is { } block)
                {
                    foreach (StatementSyntax statement in block.Statements)
                    {
                        if (statement is ReturnStatementSyntax { Expression: { } returnExpr })
                            return returnExpr.GetLocation();
                    }
                }
                break;
            case SimpleLambdaExpressionSyntax simpleLambda:
                if (simpleLambda.ExpressionBody is { } simpleExprBody)
                    return simpleExprBody.GetLocation();
                if (simpleLambda.Block is { } simpleBlock)
                {
                    foreach (StatementSyntax statement in simpleBlock.Statements)
                    {
                        if (statement is ReturnStatementSyntax { Expression: { } returnExpr })
                            return returnExpr.GetLocation();
                    }
                }
                break;
        }
        return delegateExpression.GetLocation();
    }
}
