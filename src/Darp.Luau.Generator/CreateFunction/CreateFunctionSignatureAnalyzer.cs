using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Generator.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Darp.Luau.Generator.CreateFunction;

internal static class CreateFunctionSignatureAnalyzer
{
    /// <summary> How the interceptor writes a delegate type that is not Action or Func. </summary>
    private static readonly SymbolDisplayFormat DelegateTypeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
        );

    /// <summary> The location of the callback argument, or of the call when there is none. </summary>
    private static Location GetCallbackLocation(IInvocationOperation invocationOperation)
    {
        var syntax = (InvocationExpressionSyntax)invocationOperation.Syntax;
        return syntax.ArgumentList.Arguments.Count == 0
            ? syntax.GetLocation()
            : syntax.ArgumentList.Arguments[0].Expression.GetLocation();
    }

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

        if (
            !TryAnalyzeDelegate(
                invocationOperation,
                semanticModel.Compilation,
                apiSymbols,
                out string? delegateTypeName,
                out InteropSignature signature,
                diagnostics
            )
        )
        {
            return new CreateFunctionAnalysisResult(null, diagnostics.ToImmutableArray());
        }

        return new CreateFunctionAnalysisResult(
            new CreateFunctionModel(
                new InterceptorLocationData(location.Version, location.Data),
                delegateTypeName,
                signature
            ),
            diagnostics.ToImmutableArray()
        );
    }

    /// <summary> Gets the type of the delegate the call passes, as generated code writes it, and its signature. </summary>
    private static bool TryAnalyzeDelegate(
        IInvocationOperation invocationOperation,
        Compilation compilation,
        LuauApiSymbols? apiSymbols,
        [NotNullWhen(true)] out string? delegateTypeName,
        out InteropSignature signature,
        List<Diagnostic> diagnostics
    )
    {
        delegateTypeName = null;
        signature = default;
        ITypeSymbol? delegateType = invocationOperation.Arguments.Select(x => x.Value.Type).FirstOrDefault();

        if (delegateType is not INamedTypeSymbol { DelegateInvokeMethod: { } invokeMethod } namedDelegateType)
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
                    typeDisplayName,
                    "expected a delegate type such as Action, Func<string> or a delegate type of your own"
                )
            );
            return false;
        }

        // The interceptor takes the delegate as the type the call passes, so the generated file has to name it.
        if (GetReasonGeneratedCodeCannotName(namedDelegateType, compilation) is { } reason)
        {
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.InvalidDelegateTypeDescriptor,
                    GetCallbackLocation(invocationOperation),
                    // The delegate type of the compiler has no qualified name: it is shown as the compiler shows it.
                    delegateType.ToDisplayString(
                        delegateType.CanBeReferencedByName ? SymbolDisplayFormat.FullyQualifiedFormat : null
                    ),
                    reason
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

            if (parameter.RefKind is not RefKind.None)
            {
                diagnostics.Add(
                    Diagnostic.Create(
                        DiagnosticDescriptors.UnsupportedTypeDescriptor,
                        parameterLocation,
                        parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        $"{usageDescription}, which is passed by reference (ref, in or out)"
                    )
                );
                return false;
            }

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
        if (
            awaitable is not AwaitableReturnKind.None
            && invokeMethod.ReturnNullableAnnotation is NullableAnnotation.Annotated
        )
        {
            // A callback that returns null instead of a task would fail when its result is awaited.
            diagnostics.Add(
                Diagnostic.Create(
                    DiagnosticDescriptors.UnsupportedTypeDescriptor,
                    GetReturnLocation(invocationOperation),
                    invokeMethod.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    "the result of an async callback (the task must not be nullable)"
                )
            );
            return false;
        }

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
        // The compiler infers Action or Func for a lambda, and the symbol of that type lacks the nullability of an
        // inferred return. The signature reads it from the body of the lambda and the interceptor is written from
        // the signature, so the type is too. A delegate type of its own declares its nullability and is taken as is.
        delegateTypeName = IsActionOrFunc(namedDelegateType)
            ? EmitterHelper.GetFunctionRepresentation(signature)
            : delegateType.ToDisplayString(DelegateTypeFormat);
        return true;
    }

    private static bool IsActionOrFunc(INamedTypeSymbol delegateType) =>
        delegateType
            is {
                Name: "Action" or "Func",
                ContainingType: null,
                ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
            };

    /// <summary> Why the generated file cannot write <paramref name="delegateType"/>, or null when it can. </summary>
    private static string? GetReasonGeneratedCodeCannotName(INamedTypeSymbol delegateType, Compilation compilation)
    {
        if (GetReasonTypeHasNoName(delegateType) is { } reason)
            return reason;

        // Covers the type arguments and the containing types, like the private userdata type in Func<Hidden, int>.
        return compilation.IsSymbolAccessibleWithin(delegateType, compilation.Assembly)
            ? null
            : "generated code cannot access it. Make the type and its type arguments internal or public";
    }

    /// <summary> Why no name reaches <paramref name="type"/> from another file, or null when one does. </summary>
    private static string? GetReasonTypeHasNoName(ITypeSymbol type)
    {
        switch (type)
        {
            case ITypeParameterSymbol:
                return "it uses a type parameter of the code around the call, which generated code does not have";
            case IArrayTypeSymbol array:
                return GetReasonTypeHasNoName(array.ElementType);
            case INamedTypeSymbol named:
                // A lambda that no Action or Func fits gets a delegate type of the compiler, which has no name.
                if (!named.CanBeReferencedByName)
                {
                    return "only the compiler can name it, like the delegate type of a lambda with default values, "
                        + "params or more than 16 parameters. Use a delegate type of your own";
                }

                if (named.IsFileLocal)
                    return "a file-local type is not visible to generated code";

                foreach (ITypeSymbol typeArgument in named.TypeArguments)
                {
                    if (GetReasonTypeHasNoName(typeArgument) is { } reason)
                        return reason;
                }

                return named.ContainingType is { } containingType ? GetReasonTypeHasNoName(containingType) : null;
            default:
                return null;
        }
    }

    /// <summary> Whether the callback is an async lambda or an async method. A delegate held in a variable is not seen. </summary>
    private static bool IsAsyncCallback(IInvocationOperation invocationOperation) =>
        IsAsyncCallback(invocationOperation.Arguments.FirstOrDefault()?.Value);

    private static bool IsAsyncCallback(IOperation? callback)
    {
        while (callback is IConversionOperation or IParenthesizedOperation)
        {
            callback = callback switch
            {
                IConversionOperation conversion => conversion.Operand,
                IParenthesizedOperation parenthesized => parenthesized.Operand,
                _ => null,
            };
        }

        return callback switch
        {
            // Either branch may be the callback that runs.
            IConditionalOperation conditional => IsAsyncCallback(conditional.WhenTrue)
                || IsAsyncCallback(conditional.WhenFalse),
            IDelegateCreationOperation
            {
                Target: IAnonymousFunctionOperation { Symbol.IsAsync: true }
                    or IMethodReferenceOperation { Method.IsAsync: true }
            } => true,
            _ => false,
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
