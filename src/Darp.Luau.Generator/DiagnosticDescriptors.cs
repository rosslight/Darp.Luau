using Microsoft.CodeAnalysis;

namespace Darp.Luau.Generator;

internal static class DiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor DirectInvocationRequiredDescriptor = new(
        id: "DLUAU0001",
        title: "CreateFunction must be invoked directly",
        messageFormat: "CreateFunction must be invoked directly so the compiler can intercept it; {0}",
        category: "Darp.Luau.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor UnsupportedTypeDescriptor = new(
        id: "DLUAU0002",
        title: "Unsupported type for Luau interop",
        messageFormat: "Type '{0}' used for {1} is not supported for Luau parameter/return marshalling",
        category: "Darp.Luau.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor InvalidDelegateTypeDescriptor = new(
        id: "DLUAU0003",
        title: "Invalid delegate type",
        messageFormat: "Type '{0}' cannot be used as a delegate type. Expected a concrete delegate type (e.g., Action, Func<string>, or a custom delegate).",
        category: "Darp.Luau.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor InterceptableLocationUnavailableDescriptor = new(
        id: "DLUAU0004",
        title: "CreateFunction invocation could not be intercepted",
        messageFormat: "CreateFunction invocation could not be intercepted: {0}",
        category: "Darp.Luau.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor UnsupportedReturnTupleDescriptor = new(
        id: "DLUAU0005",
        title: "Unsupported tuple return shape",
        messageFormat: "Return tuple type '{0}' is not supported: {1}",
        category: "Darp.Luau.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor LuauModuleTypeMustBePartialDescriptor = new(
        id: "DLUAU1001",
        title: "Luau module type must be partial",
        messageFormat: "Type marked with [LuauModule] must be partial",
        category: "Darp.Luau.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor LuauUserdataTypeMustBePartialDescriptor = new(
        id: "DLUAU1002",
        title: "Luau userdata type must be partial",
        messageFormat: "Type marked with [LuauUserdata] must be partial",
        category: "Darp.Luau.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor DuplicateLuauMemberNameDescriptor = new(
        id: "DLUAU1003",
        title: "Duplicate Luau member name",
        messageFormat: "Duplicate Luau member name '{0}' on {1} '{2}'",
        category: "Darp.Luau.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor UnsupportedGeneratedPropertyTypeDescriptor = new(
        id: "DLUAU1004",
        title: "Unsupported generated property type",
        messageFormat: "Member type '{0}' is not supported for generated {1} '{2}'",
        category: "Darp.Luau.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor InstanceModuleStructNotSupportedDescriptor = new(
        id: "DLUAU1005",
        title: "Instance module structs are not supported",
        messageFormat: "Instance module structs are not supported because the receiver would be copied",
        category: "Darp.Luau.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor UnsupportedGeneratedFunctionShapeDescriptor = new(
        id: "DLUAU1006",
        title: "Unsupported generated function shape",
        messageFormat: "Generated {0} '{1}' uses an unsupported parameter or return shape: {2}",
        category: "Darp.Luau.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor LuauExportPathConflictDescriptor = new(
        id: "DLUAU1007",
        title: "Luau export path conflict",
        messageFormat: "Luau export path conflict: '{0}' cannot be both a leaf member and a namespace for '{1}'",
        category: "Darp.Luau.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor InvalidLuauExportPathDescriptor = new(
        id: "DLUAU1008",
        title: "Invalid Luau export path",
        messageFormat: "Invalid Luau export path '{0}'; empty path segments are not allowed",
        category: "Darp.Luau.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor ModulePropertyMustBeReadOnlyDescriptor = new(
        id: "DLUAU1009",
        title: "Module properties must be read-only",
        messageFormat: "Exported module property '{0}' must be read-only because modules are snapshot tables after loading",
        category: "Darp.Luau.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor GeneratedUserdataManualInteropConflictDescriptor = new(
        id: "DLUAU1011",
        title: "Generated and manual userdata registration cannot mix",
        messageFormat: "Userdata type '{0}' cannot have both [LuauUserdata] and a Register method of its own: describe the type either with attributes or by hand",
        category: "Darp.Luau.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor InvalidGeneratedExportShapeDescriptor = new(
        id: "DLUAU1012",
        title: "Invalid generated export shape",
        messageFormat: "Invalid generated export shape: {0}",
        category: "Darp.Luau.Generator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor UnreachableMetamethodOverloadDescriptor = new(
        id: "DLUAU1014",
        title: "Metamethod overload is never called",
        messageFormat: "Metamethod '{0}' on '{1}' is never called: Luau uses the metamethod of the left operand, and '{2}' declares '{0}' itself",
        category: "Darp.Luau.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor LuauExportPathSegmentRequiresBracketAccessDescriptor = new(
        id: "DLUAU1013",
        title: "Luau export path segment requires bracket access",
        messageFormat: "Luau export path '{0}' contains a segment that requires bracket access and is not valid for dot-path syntax",
        category: "Darp.Luau.Generator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );
}
