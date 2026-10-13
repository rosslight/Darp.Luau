namespace Darp.Luau.Generator.Helpers;

/// <summary> The awaitable a callback returns. Luau receives its result. </summary>
internal enum AwaitableReturnKind
{
    None,
    Task,
    ValueTask,
}

/// <summary> A parameter of a managed callback. </summary>
/// <param name="Type">The type the argument is read as.</param>
/// <param name="Default">
/// The default value as C# text, which the parameter takes when the argument is nil or was not passed. Null when
/// the parameter has none.
/// </param>
internal readonly record struct InteropParameter(InteropType Type, string? Default = null)
{
    /// <summary> The default of a nullable parameter: what it gets for nil without a default. </summary>
    public const string NullDefault = "null";
}

internal readonly record struct InteropSignature(
    ImmutableEquatableArray<InteropParameter> Parameters,
    ImmutableEquatableArray<InteropType> ReturnTypes,
    AwaitableReturnKind Awaitable
);
