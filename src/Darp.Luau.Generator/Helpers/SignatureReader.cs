using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Darp.Luau.Generator.Helpers;

/// <summary> What generated code cannot read or return about the signature of a managed callback. </summary>
/// <param name="Descriptor">The error of a type, or of the shape of a returned tuple.</param>
/// <param name="ParameterIndex">The parameter the error is about. Null when it is about the return.</param>
/// <param name="Type">The type the error is about.</param>
/// <param name="Reason">What the type is used for, or what the tuple has.</param>
internal sealed record SignatureError(DiagnosticDescriptor Descriptor, int? ParameterIndex, string Type, string Reason)
{
    public Diagnostic ToDiagnostic(Location location) => Diagnostic.Create(Descriptor, location, Type, Reason);
}

/// <summary>
/// Reads what a managed callback takes and returns. It is the one place that does: the delegate of
/// <c>CreateFunction</c>, a function of a host module, and a method or a metamethod of a userdata type are read
/// here, so they take the same signatures and report the same errors.
/// </summary>
internal static class SignatureReader
{
    /// <summary> Reads the signature of <paramref name="method"/>. </summary>
    /// <param name="method">The method the callback calls. For a delegate that is its <c>Invoke</c> method.</param>
    /// <param name="isAsync">
    /// Whether the code behind <paramref name="method"/> is async. A delegate does not say that; the lambda or the
    /// method it was made from does.
    /// </param>
    /// <param name="returnOverrides">
    /// What the body of a lambda says about each value it returns. Empty when the return type says all there is.
    /// </param>
    /// <param name="apiSymbols">The types of the library.</param>
    /// <param name="signature">The signature, when the method has one generated code can call.</param>
    /// <returns>The first thing generated code cannot use, or null.</returns>
    public static SignatureError? TryRead(
        IMethodSymbol method,
        bool isAsync,
        ImmutableArray<LambdaReturnOverride> returnOverrides,
        LuauApiSymbols apiSymbols,
        out InteropSignature signature
    )
    {
        signature = default;
        if (method.ReturnsByRef || method.ReturnsByRefReadonly)
        {
            // Luau gets a copy of what a callback returns, so a reference to a variable has nothing to give it.
            return ReturnError(method.ReturnType, "the return value, which is returned by reference");
        }

        if (isAsync && method.ReturnsVoid)
        {
            // An async void callback reports success at once and runs on unobserved; nothing could await its work.
            return ReturnError(
                method.ReturnType,
                "the result of an async callback (return Task or ValueTask so the script can wait for the work)"
            );
        }

        var parameters = new List<InteropParameter>(method.Parameters.Length);
        for (int i = 0; i < method.Parameters.Length; i++)
        {
            IParameterSymbol parameter = method.Parameters[i];
            string? reason = TryReadParameter(parameter, i, apiSymbols, out InteropParameter read);
            if (reason is not null)
            {
                return new SignatureError(
                    DiagnosticDescriptors.UnsupportedTypeDescriptor,
                    i,
                    parameter.Type.ToDisplayString(),
                    reason
                );
            }

            parameters.Add(read);
        }

        AwaitableReturnKind awaitable = InteropTypeMapper.GetAwaitableKind(
            method.ReturnType,
            out ITypeSymbol? awaitedType
        );
        if (
            awaitable is not AwaitableReturnKind.None
            && method.ReturnNullableAnnotation is NullableAnnotation.Annotated
        )
        {
            // A callback that returns null instead of a task would fail when its result is awaited.
            return ReturnError(method.ReturnType, "the result of an async callback (the task must not be nullable)");
        }

        var returns = new List<InteropType>();
        ITypeSymbol? returnType = awaitable is AwaitableReturnKind.None ? method.ReturnType : awaitedType;
        if (TryReadReturns(returnType, returnOverrides, apiSymbols, returns) is { } returnError)
            return returnError;

        signature = new InteropSignature(
            parameters.ToImmutableEquatableArray(),
            returns.ToImmutableEquatableArray(),
            awaitable
        );
        return null;
    }

    /// <returns>What the parameter is that generated code cannot read. Null when it can read it.</returns>
    private static string? TryReadParameter(
        IParameterSymbol parameter,
        int index,
        LuauApiSymbols apiSymbols,
        out InteropParameter read
    )
    {
        read = default;
        string usage = parameter.Name is { Length: > 0 } ? $"parameter '{parameter.Name}'" : $"parameter #{index + 1}";
        if (parameter.RefKind is not RefKind.None)
            return $"{usage}, which is passed by reference (ref, in or out)";
        if (parameter.IsParams)
            return $"{usage}, which uses params";
        if (!InteropTypeMapper.TryMapType(parameter.Type, apiSymbols, out InteropType type))
            return usage;

        return ParameterDefault.TryRead(parameter, type, out read) is { } defaultError
            ? $"{usage}, which {defaultError}"
            : null;
    }

    /// <summary>
    /// Reads <paramref name="returnType"/>, the type Luau receives. It is null when an awaitable produces nothing.
    /// </summary>
    private static SignatureError? TryReadReturns(
        ITypeSymbol? returnType,
        ImmutableArray<LambdaReturnOverride> returnOverrides,
        LuauApiSymbols apiSymbols,
        List<InteropType> returns
    )
    {
        if (returnType is null or { SpecialType: SpecialType.System_Void })
            return null;

        if (returnType is not INamedTypeSymbol { IsTupleType: true } tupleType)
        {
            bool isNullable = returnOverrides is [{ IsNullable: true }];
            if (!TryReadReturn(returnType, isNullable, apiSymbols, out InteropType read))
                return ReturnError(returnType, "the return value");

            returns.Add(read);
            return null;
        }

        if (tupleType.TupleElements.Length > 4)
            return TupleError(tupleType, "only up to 4 tuple return values are supported");

        for (int i = 0; i < tupleType.TupleElements.Length; i++)
        {
            IFieldSymbol element = tupleType.TupleElements[i];
            if (element.Type is INamedTypeSymbol { IsTupleType: true } nestedTupleType)
                return TupleError(
                    tupleType,
                    $"nested tuple element '{nestedTupleType.ToDisplayString()}' is not supported"
                );

            bool isNullable = returnOverrides.Length > i && returnOverrides[i].IsNullable;
            if (!TryReadReturn(element.Type, isNullable, apiSymbols, out InteropType read))
                return ReturnError(element.Type, $"return tuple element '{element.Name}'");

            returns.Add(read with { TupleElementName = EmitterHelper.EscapeIdentifier(element.Name) });
        }

        return null;
    }

    private static bool TryReadReturn(
        ITypeSymbol type,
        bool isNullable,
        LuauApiSymbols apiSymbols,
        out InteropType read
    )
    {
        if (!InteropTypeMapper.TryMapType(type, apiSymbols, out read) || !InteropTypeMapper.CanBeReturned(read.Type))
            return false;
        if (!isNullable || read.IsNullable)
            return true;

        read = read with { IsNullable = true };
        return InteropTypeMapper.SupportsNullableValue(read.Type);
    }

    private static SignatureError ReturnError(ITypeSymbol type, string usage) =>
        new(DiagnosticDescriptors.UnsupportedTypeDescriptor, null, type.ToDisplayString(), usage);

    private static SignatureError TupleError(ITypeSymbol tupleType, string reason) =>
        new(DiagnosticDescriptors.UnsupportedReturnTupleDescriptor, null, tupleType.ToDisplayString(), reason);
}
