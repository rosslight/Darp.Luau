using System.Collections.Immutable;
using Darp.Luau.Generator.Helpers;
using Microsoft.CodeAnalysis;

namespace Darp.Luau.Generator.GeneratedExports;

internal enum LuauExportedTypeKind
{
    Module,
    Userdata,
}

internal enum LuauExportedMemberKind
{
    Property,
    Method,
}

internal enum LuauExportPropertyAccess
{
    Auto = 0,
    ReadOnly = 1,
    WriteOnly = 2,
    ReadWrite = 3,
}

internal sealed record SourceOrigin(string DisplayName, Location Location);

internal sealed record DiscoveredExportType(
    INamedTypeSymbol Symbol,
    LuauExportedTypeKind Kind,
    AttributeData Attribute,
    SourceOrigin Origin,
    ImmutableEquatableArray<DiscoveredExportMember> Members,
    ImmutableEquatableArray<DiscoveredExportMetamethod> Metamethods
);

internal sealed record DiscoveredExportMetamethod(IMethodSymbol Symbol, AttributeData Attribute, SourceOrigin Origin);

internal abstract record DiscoveredExportMember(string ManagedName, AttributeData Attribute, SourceOrigin Origin);

internal sealed record DiscoveredExportProperty(IPropertySymbol Symbol, AttributeData Attribute, SourceOrigin Origin)
    : DiscoveredExportMember(Symbol.Name, Attribute, Origin);

internal sealed record DiscoveredExportMethod(IMethodSymbol Symbol, AttributeData Attribute, SourceOrigin Origin)
    : DiscoveredExportMember(Symbol.Name, Attribute, Origin);

internal sealed record NormalizedExportType(
    INamedTypeSymbol Symbol,
    LuauExportedTypeKind Kind,
    string? ModuleName,
    string? UserdataTypeName,
    SourceOrigin Origin,
    ImmutableEquatableArray<NormalizedExportMember> Members,
    ImmutableEquatableArray<GeneratedMetamethodIr> Metamethods
);

internal abstract record NormalizedExportMember(
    string ManagedName,
    string LuauName,
    ImmutableEquatableArray<string> PathSegments,
    SourceOrigin Origin
)
{
    public abstract ISymbol Symbol { get; }
}

internal sealed record NormalizedExportPropertyMember(
    IPropertySymbol PropertySymbol,
    string ManagedName,
    string LuauName,
    ImmutableEquatableArray<string> PathSegments,
    SourceOrigin Origin,
    NormalizedPropertyContract Property
) : NormalizedExportMember(ManagedName, LuauName, PathSegments, Origin)
{
    public override ISymbol Symbol => PropertySymbol;
}

internal sealed record NormalizedExportMethodMember(
    IMethodSymbol MethodSymbol,
    string ManagedName,
    string LuauName,
    ImmutableEquatableArray<string> PathSegments,
    SourceOrigin Origin,
    InteropSignature Signature
) : NormalizedExportMember(ManagedName, LuauName, PathSegments, Origin)
{
    public override ISymbol Symbol => MethodSymbol;
}

internal sealed record NormalizedPropertyContract(
    NormalizedPropertyAccessor? Getter,
    NormalizedPropertyAccessor? Setter
);

internal sealed record NormalizedPropertyAccessor(InteropType Type);

internal sealed record ValidatedExportType(NormalizedExportType Type, ValidatedModuleExportNode? ModuleRoot);

internal sealed record ValidatedModuleExportNode(
    string Name,
    NormalizedExportMember? Member,
    ImmutableEquatableArray<ValidatedModuleExportNode> Children
);

internal sealed record GeneratedExportSurfaceIr(
    string ManagedTypeName,
    string? NamespaceName,
    string TypeDeclaration,
    bool IsStatic,
    string HintName,
    LuauExportedTypeKind Kind,
    string? ModuleName,
    string? UserdataTypeName,
    ImmutableEquatableArray<GeneratedExportMemberIr> Members,
    ImmutableEquatableArray<GeneratedMetamethodIr> Metamethods,
    GeneratedModuleExportNodeIr? ModuleRoot
);

internal abstract record GeneratedExportMemberIr(
    bool IsStatic,
    string ManagedName,
    string LuauName,
    ImmutableEquatableArray<string> PathSegments
);

internal sealed record GeneratedExportPropertyIr(
    bool IsStatic,
    string ManagedName,
    string LuauName,
    ImmutableEquatableArray<string> PathSegments,
    GeneratedExportAccessorIr? Getter,
    GeneratedExportAccessorIr? Setter
) : GeneratedExportMemberIr(IsStatic, ManagedName, LuauName, PathSegments);

internal sealed record GeneratedExportMethodIr(
    string ContainingTypeName,
    bool IsStatic,
    string ManagedName,
    string LuauName,
    ImmutableEquatableArray<string> PathSegments,
    InteropSignature Signature
) : GeneratedExportMemberIr(IsStatic, ManagedName, LuauName, PathSegments);

internal sealed record GeneratedExportAccessorIr(InteropType Type);

internal enum MetamethodCallKind
{
    StaticMethod,
    InstanceMethod,
    BinaryOperator,
    UnaryOperator,
}

/// <param name="Name">The name of the <c>LuauMetamethod</c> value.</param>
/// <param name="Overloads">The methods that implement it. Luau can tell their operands apart by type.</param>
internal sealed record GeneratedMetamethodIr(
    string Name,
    ImmutableEquatableArray<GeneratedMetamethodOverloadIr> Overloads
);

/// <param name="CallKind">How generated code calls the method.</param>
/// <param name="CallTarget">The qualified name of a static method, the name of an instance method, or an operator.</param>
/// <param name="Signature">The operands as Luau passes them, the instance first for an instance method.</param>
internal sealed record GeneratedMetamethodOverloadIr(
    MetamethodCallKind CallKind,
    string CallTarget,
    InteropSignature Signature
);

internal sealed record GeneratedModuleExportNodeIr(
    string Name,
    GeneratedExportMemberIr? Member,
    ImmutableEquatableArray<GeneratedModuleExportNodeIr> Children
);

internal sealed record GeneratedExportsTypeAnalysis(
    GeneratedExportSurfaceIr? Model,
    ImmutableArray<Diagnostic> Diagnostics,
    bool CanEmitSource
);
