using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Utils;

namespace Darp.Luau.Internal;

/// <summary>
/// A little helper to track the validity of a Luau call frame.
/// Initialized using <see cref="LuauState.BeginLuauCallFrame"/>."/>
/// </summary>
internal readonly struct LuauCallFrame : IDisposable
{
    private readonly ulong _id;

    internal LuauCallFrame(LuauState state, ulong id)
    {
        State = state;
        _id = id;
    }

    public LuauState? State { get; }

    [MemberNotNull(nameof(State))]
    public void ThrowIfDisposed()
    {
        if (_id is 0)
            throw new ObjectDisposedException(nameof(LuauArgs), "The Luau argument frame is no longer valid.");
        State.ThrowIfDisposed();
        State.ThrowIfLuauCallFrameDisposed(_id);
    }

    public void Dispose() => State?.EndLuauCallFrame(_id);
}
