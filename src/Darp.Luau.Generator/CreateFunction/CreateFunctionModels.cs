using System.Collections.Immutable;
using Darp.Luau.Generator.Helpers;

namespace Darp.Luau.Generator.CreateFunction;

internal readonly record struct InterceptorLocationData(int Version, string Data);

/// <param name="Location">The call the interceptor replaces.</param>
/// <param name="DelegateType">The type of the delegate the call passes, as generated code writes it.</param>
/// <param name="Signature">What the delegate takes and returns.</param>
internal sealed record CreateFunctionModel(
    InterceptorLocationData Location,
    string DelegateType,
    InteropSignature Signature
);

internal sealed record CreateFunctionAnalysisResult(
    CreateFunctionModel? Model,
    ImmutableArray<Microsoft.CodeAnalysis.Diagnostic> Diagnostics
);
