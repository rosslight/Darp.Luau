using System.Collections.Immutable;
using Darp.Luau.Generator.Helpers;
using Microsoft.CodeAnalysis;

namespace Darp.Luau.Generator.GeneratedExports;

internal enum LuauExportedTypeKind
{
    Module,
    Userdata,
}

internal enum LuauExportPropertyAccess
{
    Auto = 0,
    ReadOnly = 1,
    WriteOnly = 2,
    ReadWrite = 3,
}

/// <summary> The declaration of the partial type that generated code adds to. </summary>
/// <param name="ManagedTypeName">The qualified name of the type.</param>
/// <param name="NamespaceName">The namespace of the type, or null for the global namespace.</param>
/// <param name="TypeDeclaration">The modifiers, the keyword and the name, as the generated part repeats them.</param>
/// <param name="IsStatic">Whether the type is static.</param>
/// <param name="HintName">The name of the generated file.</param>
internal sealed record GeneratedExportTypeIr(
    string ManagedTypeName,
    string? NamespaceName,
    string TypeDeclaration,
    bool IsStatic,
    string HintName
);

/// <summary> A host module, as the generator emits it. </summary>
/// <param name="Type">The type marked with <c>[LuauModule]</c>.</param>
/// <param name="ModuleName">The name scripts pass to <c>require</c>.</param>
/// <param name="Root">The table scripts get. Its children are the members of the module.</param>
internal sealed record GeneratedModuleIr(
    GeneratedExportTypeIr Type,
    string ModuleName,
    GeneratedModuleExportNodeIr Root
);

/// <summary> A userdata type, as the generator emits it. </summary>
/// <param name="Type">The type marked with <c>[LuauUserdata]</c>.</param>
/// <param name="TypeName">The name scripts see for a value of the type.</param>
/// <param name="Members">The members of the instance and of the static side.</param>
/// <param name="Metamethods">The metamethods of the type.</param>
internal sealed record GeneratedUserdataIr(
    GeneratedExportTypeIr Type,
    string TypeName,
    ImmutableEquatableArray<GeneratedExportMemberIr> Members,
    ImmutableEquatableArray<GeneratedMetamethodIr> Metamethods
);

/// <param name="IsStatic">Whether the member is static.</param>
/// <param name="ManagedName">The name generated code refers to the member by.</param>
/// <param name="LuauName">The name scripts refer to the member by. For a member of a module it is a path.</param>
internal abstract record GeneratedExportMemberIr(bool IsStatic, string ManagedName, string LuauName);

/// <param name="IsStatic">Whether the property is static.</param>
/// <param name="ManagedName">The name generated code reads the property by, qualified for a static property.</param>
/// <param name="LuauName">The name scripts refer to the property by.</param>
/// <param name="Type">The type of the property.</param>
/// <param name="HasGetter">Whether scripts can read it. False only for a write-only property of a userdata type.</param>
/// <param name="HasSetter">Whether scripts can write it. Only an instance property of a userdata type can.</param>
internal sealed record GeneratedExportPropertyIr(
    bool IsStatic,
    string ManagedName,
    string LuauName,
    InteropType Type,
    bool HasGetter,
    bool HasSetter
) : GeneratedExportMemberIr(IsStatic, ManagedName, LuauName);

internal sealed record GeneratedExportMethodIr(
    string ContainingTypeName,
    bool IsStatic,
    string ManagedName,
    string LuauName,
    InteropSignature Signature
) : GeneratedExportMemberIr(IsStatic, ManagedName, LuauName);

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

/// <summary> An entry of a module table: a member, or a table of further entries. </summary>
/// <param name="Name">The key of the entry in its table.</param>
/// <param name="Member">The member, or null for a table.</param>
/// <param name="Children">The entries of the table.</param>
internal sealed record GeneratedModuleExportNodeIr(
    string Name,
    GeneratedExportMemberIr? Member,
    ImmutableEquatableArray<GeneratedModuleExportNodeIr> Children
);

/// <summary> A member together with what only the diagnostics of its type need. </summary>
/// <param name="Member">The member, as the generator emits it.</param>
/// <param name="PathSegments">The Luau name, split at its dots.</param>
/// <param name="Location">Where the member is declared.</param>
internal sealed record AnalyzedExportMember(
    GeneratedExportMemberIr Member,
    ImmutableArray<string> PathSegments,
    Location Location
);
