namespace Darp.Luau.Generator.Helpers;

/// <summary> The awaitable a callback returns. Luau receives its result. </summary>
internal enum AwaitableReturnKind
{
    None,
    Task,
    ValueTask,
}

internal readonly record struct InteropSignature(
    ImmutableEquatableArray<InteropType> Parameters,
    ImmutableEquatableArray<InteropType> ReturnTypes,
    AwaitableReturnKind Awaitable
);
