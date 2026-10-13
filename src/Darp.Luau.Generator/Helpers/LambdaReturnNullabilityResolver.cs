using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Darp.Luau.Generator.Helpers;

internal readonly record struct LambdaReturnOverride(bool IsNullable);

internal static class LambdaReturnNullabilityResolver
{
    /// <summary>
    /// Reads nullable-reference return information directly from lambda bodies.
    /// Roslyn's inferred delegate type for <c>CreateFunction(...)</c> can otherwise drop <c>?</c>
    /// for plain lambdas, which would merge nullable and non-nullable interceptors.
    /// </summary>
    internal static ImmutableArray<LambdaReturnOverride> GetReturnOverrides(IInvocationOperation invocationOperation)
    {
        var syntax = (InvocationExpressionSyntax)invocationOperation.Syntax;
        if (syntax.ArgumentList.Arguments.Count == 0 || invocationOperation.SemanticModel is null)
            return [];

        LambdaExpressionSyntax? lambdaSyntax = syntax.ArgumentList.Arguments[0].Expression switch
        {
            ParenthesizedLambdaExpressionSyntax parenthesizedLambda => parenthesizedLambda,
            SimpleLambdaExpressionSyntax simpleLambda => simpleLambda,
            _ => null,
        };
        if (lambdaSyntax is null)
            return [];

        LambdaReturnOverride[]? overrides = null;
        foreach (ExpressionSyntax returnExpression in GetReturnExpressions(lambdaSyntax))
        {
            TypeInfo typeInfo = invocationOperation.SemanticModel.GetTypeInfo(returnExpression);
            ITypeSymbol? returnType = typeInfo.ConvertedType ?? typeInfo.Type;
            if (returnType is null)
                continue;

            if (returnType is INamedTypeSymbol { IsTupleType: true } tupleType)
            {
                overrides ??= new LambdaReturnOverride[tupleType.TupleElements.Length];
                if (overrides.Length != tupleType.TupleElements.Length)
                    return [];

                for (int i = 0; i < tupleType.TupleElements.Length; i++)
                {
                    bool isNullable = tupleType.TupleElements[i].NullableAnnotation is NullableAnnotation.Annotated;
                    overrides[i] = new LambdaReturnOverride(overrides[i].IsNullable || isNullable);
                }

                continue;
            }

            overrides ??= new LambdaReturnOverride[1];
            if (overrides.Length != 1)
                return [];

            NullableAnnotation annotation = typeInfo.ConvertedType is null
                ? typeInfo.Nullability.Annotation
                : typeInfo.ConvertedNullability.Annotation;
            overrides[0] = new LambdaReturnOverride(
                overrides[0].IsNullable
                    || annotation is NullableAnnotation.Annotated
                    || returnExpression.IsKind(SyntaxKind.NullLiteralExpression)
            );
        }

        return overrides?.ToImmutableArray() ?? [];
    }

    /// <summary>
    /// Returns the lambda's top-level return expressions without descending into nested lambdas.
    /// </summary>
    private static IEnumerable<ExpressionSyntax> GetReturnExpressions(LambdaExpressionSyntax lambdaSyntax)
    {
        if (lambdaSyntax.ExpressionBody is { } expressionBody)
        {
            yield return expressionBody;
            yield break;
        }

        if (lambdaSyntax.Block is null)
            yield break;

        foreach (
            ReturnStatementSyntax returnStatement in lambdaSyntax
                .Block.DescendantNodes(static node =>
                    node is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax
                )
                .OfType<ReturnStatementSyntax>()
        )
        {
            if (returnStatement.Expression is { } expression)
                yield return expression;
        }
    }
}
