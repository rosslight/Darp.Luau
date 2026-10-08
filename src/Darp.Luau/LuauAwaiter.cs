using Darp.Luau.Internal;

namespace Darp.Luau;

/// <summary>
/// Lets a managed callback finish later. Get it from <see cref="LuauArgs.TryGetAwaiter"/> before you start the work,
/// and return what <c>Await</c> gives you.
/// </summary>
/// <remarks>
/// <para>
/// A pending result suspends the calling coroutine until the work completes. Asking first means that work which
/// could not be awaited is never started.
/// </para>
/// <para>
/// Read every argument before the first <c>await</c>: <see cref="LuauArgs"/> and borrowed views end with the
/// callback. Promote values you need afterwards with <c>ToOwned()</c>.
/// </para>
/// <para>
/// Work that ends with <see cref="OperationCanceledException"/> after the token of the host call was cancelled
/// cancels the host call and finishes the coroutine. Any other failure becomes a Luau error at the call site.
/// </para>
/// </remarks>
public readonly ref struct LuauAwaiter
{
    private readonly bool _isGranted;

    internal LuauAwaiter(bool isGranted) => _isGranted = isGranted;

    /// <summary> Creates a callback result that completes when <paramref name="pending"/> completes. </summary>
    /// <param name="pending">The work that produces the actual result.</param>
    /// <returns>
    /// The result of <paramref name="pending"/> when it has already completed successfully; otherwise a pending result.
    /// </returns>
    public LuauReturn Await(ValueTask<LuauReturn> pending)
    {
        ThrowIfNotGranted();
        return pending.IsCompletedSuccessfully
            ? LuauReturn.FromCompleted(pending.Result)
            : LuauReturn.FromPending(new PendingWork(pending.AsTask()));
    }

    /// <summary> Creates a callback result that returns no values when <paramref name="pending"/> completes. </summary>
    /// <param name="pending">The work to wait for.</param>
    public LuauReturn Await(ValueTask pending)
    {
        ThrowIfNotGranted();
        return pending.IsCompletedSuccessfully
            ? LuauReturn.Ok()
            : LuauReturn.FromPending(PendingWork.Create(pending.AsTask()));
    }

    /// <summary>
    /// Creates a callback result that completes with what <paramref name="complete"/> makes of the result of
    /// <paramref name="pending"/>.
    /// </summary>
    /// <param name="pending">The work that produces a value.</param>
    /// <param name="complete">
    /// Converts the value into the actual result. It runs where the state may be used, so it can return Luau references.
    /// An exception it throws becomes a Luau error at the call site.
    /// </param>
    /// <typeparam name="T">The type of the value <paramref name="pending"/> produces.</typeparam>
    public LuauReturn Await<T>(ValueTask<T> pending, Func<T, LuauReturn> complete)
    {
        ThrowIfNotGranted();
        ArgumentNullException.ThrowIfNull(complete);
        return pending.IsCompletedSuccessfully
            ? LuauReturn.FromCompleted(complete(pending.Result))
            : LuauReturn.FromPending(PendingWork.Create(pending.AsTask(), complete));
    }

    private void ThrowIfNotGranted()
    {
        if (!_isGranted)
            throw new InvalidOperationException(
                $"Get the awaiter from {nameof(LuauArgs)}.{nameof(LuauArgs.TryGetAwaiter)}."
            );
    }
}
