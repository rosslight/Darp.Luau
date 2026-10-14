using System.Collections.Immutable;
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

    public static CreateFunctionAnalysisResult Analyze(
        IInvocationOperation invocationOperation,
        LuauApiSymbols apiSymbols
    )
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
        LuauApiSymbols apiSymbols,
        out string? delegateTypeName,
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

        // Generated code casts the delegate to its type. A type with a name is written by it. A type the compiler
        // made is the type of a lambda that generated code writes with the same signature.
        string? reason = namedDelegateType.CanBeReferencedByName
            ? GetReasonGeneratedCodeCannotName(namedDelegateType, compilation)
            : GetReasonNoExampleGetsTheType(invokeMethod, compilation);
        if (reason is not null)
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

        SignatureError? error = SignatureReader.TryRead(
            invokeMethod,
            IsAsyncCallback(invocationOperation),
            LambdaReturnNullabilityResolver.GetReturnOverrides(invocationOperation),
            apiSymbols,
            out signature
        );
        if (error is not null)
        {
            Location errorLocation = error.ParameterIndex is { } parameterIndex
                ? GetParameterLocation(invocationOperation, parameterIndex)
                : GetReturnLocation(invocationOperation);
            diagnostics.Add(error.ToDiagnostic(errorLocation));
            return false;
        }

        // The compiler infers Action or Func for a lambda, and the symbol of that type lacks the nullability of an
        // inferred return. The signature reads it from the body of the lambda and the interceptor is written from
        // the signature, so the type is too. A delegate type of its own declares its nullability and is taken as is.
        if (namedDelegateType.CanBeReferencedByName)
        {
            delegateTypeName = IsActionOrFunc(namedDelegateType)
                ? EmitterHelper.GetFunctionRepresentation(signature)
                : delegateType.ToDisplayString(DelegateTypeFormat);
        }
        return true;
    }

    private static bool IsActionOrFunc(INamedTypeSymbol delegateType) =>
        delegateType
            is {
                Name: "Action" or "Func",
                ContainingType: null,
                ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
            };

    /// <summary>
    /// Why no lambda that generated code writes gets the delegate type the compiler made for a callback, or null
    /// when one does.
    /// </summary>
    private static string? GetReasonNoExampleGetsTheType(IMethodSymbol invokeMethod, Compilation compilation)
    {
        foreach (IParameterSymbol parameter in invokeMethod.Parameters)
        {
            // With a scoped parameter the compiler tells its delegate types apart by more than a signature says.
            // A params span is scoped too, and is rejected for being params.
            if (parameter is { ScopedKind: not ScopedKind.None, IsParams: false })
                return "it has a scoped parameter, so only the compiler can name it. Use a delegate type of your own";

            // "T value = default" of a generic method that the call closes over a number. Its constant is not the
            // one that "int value = default" has, so the compiler made another delegate type for it.
            if (
                parameter is { HasExplicitDefaultValue: true, ExplicitDefaultValue: null, Type.IsValueType: true }
                && (parameter.Type.SpecialType is not SpecialType.None || parameter.Type.TypeKind is TypeKind.Enum)
            )
            {
                return "a default value of it is the default of a type parameter, so only the compiler can name it. "
                    + "Use a delegate type of your own";
            }
        }

        foreach (ITypeSymbol type in invokeMethod.Parameters.Select(static x => x.Type).Append(invokeMethod.ReturnType))
        {
            if (GetReasonGeneratedCodeCannotName(type, compilation) is { } reason)
                return $"its signature has the type '{type.ToDisplayString()}', and {reason}";
        }

        return null;
    }

    /// <summary> Why the generated file cannot write <paramref name="type"/>, or null when it can. </summary>
    private static string? GetReasonGeneratedCodeCannotName(ITypeSymbol type, Compilation compilation)
    {
        if (GetReasonTypeHasNoName(type) is { } reason)
            return reason;

        // Covers the type arguments and the containing types, like the private userdata type in Func<Hidden, int>.
        return compilation.IsSymbolAccessibleWithin(type, compilation.Assembly)
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
