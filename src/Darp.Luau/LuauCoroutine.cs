using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Internal;
using Darp.Luau.Utils;

namespace Darp.Luau;

/// <summary>
/// Represents an owned Luau coroutine reference stored in the registry.
/// </summary>
/// <remarks>
/// A coroutine runs a function that can suspend itself with <c>coroutine.yield</c> and continue where it left off
/// when it is resumed again. Create one with <see cref="LuauState.CreateCoroutine(LuauFunction)"/> or read one
/// that a script created with <c>coroutine.create</c>.
/// </remarks>
[SuppressMessage(
    "Performance",
    "CA1815:Override equals and operator equals on value types",
    Justification = "This wrapper is an ownership handle; custom value equality would imply Lua identity semantics the API does not guarantee."
)]
public readonly struct LuauCoroutine : ILuauReference
{
    private readonly LuauState? _state;
    private readonly ulong _handle;

    /// <summary>
    /// Gets whether this wrapper no longer points to a tracked Luau coroutine reference.
    /// </summary>
    public bool IsDisposed => !_state.IsReferenceValid(_handle);

    /// <summary> Gets the current status of the coroutine. </summary>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked or the state is disposed.</exception>
    public unsafe LuauCoroutineStatus Status
    {
        get
        {
            _state.ThrowIfDisposed();
            return CoroutineDriver.GetStatus(_state, CoroutineDriver.GetCoroutine(_state, _handle));
        }
    }

    /// <summary>
    /// Do not initialize directly. Create coroutines through <see cref="LuauState"/> APIs.
    /// </summary>
    [Obsolete("Do not initialize the LuauCoroutine. Create using the LuauState instead", true)]
    public LuauCoroutine() { }

    internal LuauCoroutine(LuauState? state, ulong handle)
    {
        _state = state;
        _handle = handle;
    }

    /// <summary> Resumes the coroutine until it yields or finishes and ignores the values it yields or returns. </summary>
    /// <param name="args">The values passed to the coroutine: its arguments when it starts, otherwise the results of <c>coroutine.yield</c>.</param>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked or the state is disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the coroutine is not <see cref="LuauCoroutineStatus.Suspended"/>.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports an error inside the coroutine.</exception>
    public void Resume(params RefEnumerable<IntoLuau> args) =>
        _ = ResumeCore(args, LuauFunctionInvokeCore.IgnoreResults);

    /// <summary> Resumes the coroutine until it yields or finishes and converts the first value it yields or returns. </summary>
    /// <param name="args">The values passed to the coroutine: its arguments when it starts, otherwise the results of <c>coroutine.yield</c>.</param>
    /// <typeparam name="TR">Managed type to convert to.</typeparam>
    /// <returns>The first value converted to <typeparamref name="TR"/>.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked or the state is disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the coroutine is not <see cref="LuauCoroutineStatus.Suspended"/>.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports an error inside the coroutine.</exception>
    /// <exception cref="InvalidCastException">Thrown when a value cannot be converted to the requested managed type.</exception>
    public TR Resume<TR>(params RefEnumerable<IntoLuau> args) =>
        ResumeCore(args, LuauFunctionInvokeCore.ResultSelector<TR>);

    /// <summary> Resumes the coroutine until it yields or finishes and converts the first two values it yields or returns. </summary>
    /// <param name="args">The values passed to the coroutine: its arguments when it starts, otherwise the results of <c>coroutine.yield</c>.</param>
    /// <typeparam name="TR1">Managed type to convert to.</typeparam>
    /// <typeparam name="TR2">Managed type to convert to.</typeparam>
    /// <returns>Two values (additional values will be ignored).</returns>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked or the state is disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the coroutine is not <see cref="LuauCoroutineStatus.Suspended"/>.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports an error inside the coroutine.</exception>
    /// <exception cref="InvalidCastException">Thrown when a value cannot be converted to the requested managed type.</exception>
    public (TR1, TR2) Resume<TR1, TR2>(params RefEnumerable<IntoLuau> args) =>
        ResumeCore(args, LuauFunctionInvokeCore.ResultSelector<TR1, TR2>);

    /// <summary> Resumes the coroutine until it yields or finishes and converts the first three values it yields or returns. </summary>
    /// <param name="args">The values passed to the coroutine: its arguments when it starts, otherwise the results of <c>coroutine.yield</c>.</param>
    /// <typeparam name="TR1">Managed type to convert to.</typeparam>
    /// <typeparam name="TR2">Managed type to convert to.</typeparam>
    /// <typeparam name="TR3">Managed type to convert to.</typeparam>
    /// <returns>Three values (additional values will be ignored).</returns>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked or the state is disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the coroutine is not <see cref="LuauCoroutineStatus.Suspended"/>.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports an error inside the coroutine.</exception>
    /// <exception cref="InvalidCastException">Thrown when a value cannot be converted to the requested managed type.</exception>
    public (TR1, TR2, TR3) Resume<TR1, TR2, TR3>(params RefEnumerable<IntoLuau> args) =>
        ResumeCore(args, LuauFunctionInvokeCore.ResultSelector<TR1, TR2, TR3>);

    /// <summary> Resumes the coroutine until it yields or finishes and converts the first four values it yields or returns. </summary>
    /// <param name="args">The values passed to the coroutine: its arguments when it starts, otherwise the results of <c>coroutine.yield</c>.</param>
    /// <typeparam name="TR1">Managed type to convert to.</typeparam>
    /// <typeparam name="TR2">Managed type to convert to.</typeparam>
    /// <typeparam name="TR3">Managed type to convert to.</typeparam>
    /// <typeparam name="TR4">Managed type to convert to.</typeparam>
    /// <returns>Four values (additional values will be ignored).</returns>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked or the state is disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the coroutine is not <see cref="LuauCoroutineStatus.Suspended"/>.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports an error inside the coroutine.</exception>
    /// <exception cref="InvalidCastException">Thrown when a value cannot be converted to the requested managed type.</exception>
    public (TR1, TR2, TR3, TR4) Resume<TR1, TR2, TR3, TR4>(params RefEnumerable<IntoLuau> args) =>
        ResumeCore(args, LuauFunctionInvokeCore.ResultSelector<TR1, TR2, TR3, TR4>);

    /// <summary> Resumes the coroutine until it yields or finishes and returns all values it yields or returns. </summary>
    /// <param name="args">The values passed to the coroutine: its arguments when it starts, otherwise the results of <c>coroutine.yield</c>.</param>
    /// <returns>All values as raw <see cref="LuauValue"/> instances.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked or the state is disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the coroutine is not <see cref="LuauCoroutineStatus.Suspended"/>.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports an error inside the coroutine.</exception>
    public LuauValue[] ResumeMulti(params RefEnumerable<IntoLuau> args) =>
        ResumeCore(args, LuauFunctionInvokeCore.ResultSelectorMulti);

    /// <summary> Resumes the coroutine until it yields or finishes and ignores the values it yields or returns. </summary>
    /// <param name="args">The values passed to the coroutine: its arguments when it starts, otherwise the results of <c>coroutine.yield</c>.</param>
    /// <returns>A task that completes when the coroutine yields or finishes.</returns>
    /// <remarks>
    /// Managed callbacks may return <see cref="LuauReturn.Await"/> to suspend the coroutine
    /// until their work completes; it is then resumed with their result. The returned task completes when the
    /// coroutine yields on its own or finishes.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked or the state is disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the coroutine is not <see cref="LuauCoroutineStatus.Suspended"/>.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports an error inside the coroutine.</exception>
    public ValueTask ResumeAsync(params RefEnumerable<IntoLuau> args) => ResumeAsync(args, CancellationToken.None);

    /// <inheritdoc cref="ResumeAsync(RefEnumerable{IntoLuau})"/>
    /// <param name="args">The values passed to the coroutine: its arguments when it starts, otherwise the results of <c>coroutine.yield</c>.</param>
    /// <param name="cancellationToken">
    /// Available to managed callbacks as <see cref="LuauArgs.CancellationToken"/>. Cancellation is cooperative.
    /// </param>
    /// <exception cref="OperationCanceledException">Thrown when the work of an awaiting managed callback is canceled.</exception>
    public ValueTask ResumeAsync(scoped RefEnumerable<IntoLuau> args, CancellationToken cancellationToken) =>
        LuauFunctionInvokeCore.WithoutResult(
            ResumeCoreAsync(args, LuauFunctionInvokeCore.IgnoreResults, cancellationToken)
        );

    /// <summary> Resumes the coroutine until it yields or finishes and converts the first value it yields or returns. </summary>
    /// <param name="args">The values passed to the coroutine: its arguments when it starts, otherwise the results of <c>coroutine.yield</c>.</param>
    /// <typeparam name="TR">Managed type to convert to.</typeparam>
    /// <returns>The first value converted to <typeparamref name="TR"/>.</returns>
    /// <remarks>
    /// Managed callbacks may return <see cref="LuauReturn.Await"/> to suspend the coroutine
    /// until their work completes; it is then resumed with their result. The returned task completes when the
    /// coroutine yields on its own or finishes.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked or the state is disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the coroutine is not <see cref="LuauCoroutineStatus.Suspended"/>.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports an error inside the coroutine.</exception>
    /// <exception cref="InvalidCastException">Thrown when a value cannot be converted to the requested managed type.</exception>
    public ValueTask<TR> ResumeAsync<TR>(params RefEnumerable<IntoLuau> args) =>
        ResumeAsync<TR>(args, CancellationToken.None);

    /// <inheritdoc cref="ResumeAsync{TR}(RefEnumerable{IntoLuau})"/>
    /// <param name="args">The values passed to the coroutine: its arguments when it starts, otherwise the results of <c>coroutine.yield</c>.</param>
    /// <param name="cancellationToken">
    /// Available to managed callbacks as <see cref="LuauArgs.CancellationToken"/>. Cancellation is cooperative.
    /// </param>
    /// <exception cref="OperationCanceledException">Thrown when the work of an awaiting managed callback is canceled.</exception>
    public ValueTask<TR> ResumeAsync<TR>(scoped RefEnumerable<IntoLuau> args, CancellationToken cancellationToken) =>
        ResumeCoreAsync(args, LuauFunctionInvokeCore.ResultSelector<TR>, cancellationToken);

    /// <summary> Resumes the coroutine until it yields or finishes and converts the first two values it yields or returns. </summary>
    /// <param name="args">The values passed to the coroutine: its arguments when it starts, otherwise the results of <c>coroutine.yield</c>.</param>
    /// <typeparam name="TR1">Managed type to convert to.</typeparam>
    /// <typeparam name="TR2">Managed type to convert to.</typeparam>
    /// <returns>Two values (additional values will be ignored).</returns>
    /// <remarks>
    /// Managed callbacks may return <see cref="LuauReturn.Await"/> to suspend the coroutine
    /// until their work completes; it is then resumed with their result. The returned task completes when the
    /// coroutine yields on its own or finishes.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked or the state is disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the coroutine is not <see cref="LuauCoroutineStatus.Suspended"/>.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports an error inside the coroutine.</exception>
    /// <exception cref="InvalidCastException">Thrown when a value cannot be converted to the requested managed type.</exception>
    public ValueTask<(TR1, TR2)> ResumeAsync<TR1, TR2>(params RefEnumerable<IntoLuau> args) =>
        ResumeAsync<TR1, TR2>(args, CancellationToken.None);

    /// <inheritdoc cref="ResumeAsync{TR1, TR2}(RefEnumerable{IntoLuau})"/>
    /// <param name="args">The values passed to the coroutine: its arguments when it starts, otherwise the results of <c>coroutine.yield</c>.</param>
    /// <param name="cancellationToken">
    /// Available to managed callbacks as <see cref="LuauArgs.CancellationToken"/>. Cancellation is cooperative.
    /// </param>
    /// <exception cref="OperationCanceledException">Thrown when the work of an awaiting managed callback is canceled.</exception>
    public ValueTask<(TR1, TR2)> ResumeAsync<TR1, TR2>(
        scoped RefEnumerable<IntoLuau> args,
        CancellationToken cancellationToken
    ) => ResumeCoreAsync(args, LuauFunctionInvokeCore.ResultSelector<TR1, TR2>, cancellationToken);

    /// <summary> Resumes the coroutine until it yields or finishes and converts the first three values it yields or returns. </summary>
    /// <param name="args">The values passed to the coroutine: its arguments when it starts, otherwise the results of <c>coroutine.yield</c>.</param>
    /// <typeparam name="TR1">Managed type to convert to.</typeparam>
    /// <typeparam name="TR2">Managed type to convert to.</typeparam>
    /// <typeparam name="TR3">Managed type to convert to.</typeparam>
    /// <returns>Three values (additional values will be ignored).</returns>
    /// <remarks>
    /// Managed callbacks may return <see cref="LuauReturn.Await"/> to suspend the coroutine
    /// until their work completes; it is then resumed with their result. The returned task completes when the
    /// coroutine yields on its own or finishes.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked or the state is disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the coroutine is not <see cref="LuauCoroutineStatus.Suspended"/>.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports an error inside the coroutine.</exception>
    /// <exception cref="InvalidCastException">Thrown when a value cannot be converted to the requested managed type.</exception>
    public ValueTask<(TR1, TR2, TR3)> ResumeAsync<TR1, TR2, TR3>(params RefEnumerable<IntoLuau> args) =>
        ResumeAsync<TR1, TR2, TR3>(args, CancellationToken.None);

    /// <inheritdoc cref="ResumeAsync{TR1, TR2, TR3}(RefEnumerable{IntoLuau})"/>
    /// <param name="args">The values passed to the coroutine: its arguments when it starts, otherwise the results of <c>coroutine.yield</c>.</param>
    /// <param name="cancellationToken">
    /// Available to managed callbacks as <see cref="LuauArgs.CancellationToken"/>. Cancellation is cooperative.
    /// </param>
    /// <exception cref="OperationCanceledException">Thrown when the work of an awaiting managed callback is canceled.</exception>
    public ValueTask<(TR1, TR2, TR3)> ResumeAsync<TR1, TR2, TR3>(
        scoped RefEnumerable<IntoLuau> args,
        CancellationToken cancellationToken
    ) => ResumeCoreAsync(args, LuauFunctionInvokeCore.ResultSelector<TR1, TR2, TR3>, cancellationToken);

    /// <summary> Resumes the coroutine until it yields or finishes and converts the first four values it yields or returns. </summary>
    /// <param name="args">The values passed to the coroutine: its arguments when it starts, otherwise the results of <c>coroutine.yield</c>.</param>
    /// <typeparam name="TR1">Managed type to convert to.</typeparam>
    /// <typeparam name="TR2">Managed type to convert to.</typeparam>
    /// <typeparam name="TR3">Managed type to convert to.</typeparam>
    /// <typeparam name="TR4">Managed type to convert to.</typeparam>
    /// <returns>Four values (additional values will be ignored).</returns>
    /// <remarks>
    /// Managed callbacks may return <see cref="LuauReturn.Await"/> to suspend the coroutine
    /// until their work completes; it is then resumed with their result. The returned task completes when the
    /// coroutine yields on its own or finishes.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked or the state is disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the coroutine is not <see cref="LuauCoroutineStatus.Suspended"/>.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports an error inside the coroutine.</exception>
    /// <exception cref="InvalidCastException">Thrown when a value cannot be converted to the requested managed type.</exception>
    public ValueTask<(TR1, TR2, TR3, TR4)> ResumeAsync<TR1, TR2, TR3, TR4>(params RefEnumerable<IntoLuau> args) =>
        ResumeAsync<TR1, TR2, TR3, TR4>(args, CancellationToken.None);

    /// <inheritdoc cref="ResumeAsync{TR1, TR2, TR3, TR4}(RefEnumerable{IntoLuau})"/>
    /// <param name="args">The values passed to the coroutine: its arguments when it starts, otherwise the results of <c>coroutine.yield</c>.</param>
    /// <param name="cancellationToken">
    /// Available to managed callbacks as <see cref="LuauArgs.CancellationToken"/>. Cancellation is cooperative.
    /// </param>
    /// <exception cref="OperationCanceledException">Thrown when the work of an awaiting managed callback is canceled.</exception>
    public ValueTask<(TR1, TR2, TR3, TR4)> ResumeAsync<TR1, TR2, TR3, TR4>(
        scoped RefEnumerable<IntoLuau> args,
        CancellationToken cancellationToken
    ) => ResumeCoreAsync(args, LuauFunctionInvokeCore.ResultSelector<TR1, TR2, TR3, TR4>, cancellationToken);

    /// <summary> Resumes the coroutine until it yields or finishes and returns all values it yields or returns. </summary>
    /// <param name="args">The values passed to the coroutine: its arguments when it starts, otherwise the results of <c>coroutine.yield</c>.</param>
    /// <returns>All values as raw <see cref="LuauValue"/> instances.</returns>
    /// <remarks>
    /// Managed callbacks may return <see cref="LuauReturn.Await"/> to suspend the coroutine
    /// until their work completes; it is then resumed with their result. The returned task completes when the
    /// coroutine yields on its own or finishes.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked or the state is disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the coroutine is not <see cref="LuauCoroutineStatus.Suspended"/>.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports an error inside the coroutine.</exception>
    public ValueTask<LuauValue[]> ResumeMultiAsync(params RefEnumerable<IntoLuau> args) =>
        ResumeMultiAsync(args, CancellationToken.None);

    /// <inheritdoc cref="ResumeMultiAsync(RefEnumerable{IntoLuau})"/>
    /// <param name="args">The values passed to the coroutine: its arguments when it starts, otherwise the results of <c>coroutine.yield</c>.</param>
    /// <param name="cancellationToken">
    /// Available to managed callbacks as <see cref="LuauArgs.CancellationToken"/>. Cancellation is cooperative.
    /// </param>
    /// <exception cref="OperationCanceledException">Thrown when the work of an awaiting managed callback is canceled.</exception>
    public ValueTask<LuauValue[]> ResumeMultiAsync(
        scoped RefEnumerable<IntoLuau> args,
        CancellationToken cancellationToken
    ) => ResumeCoreAsync(args, LuauFunctionInvokeCore.ResultSelectorMulti, cancellationToken);

    /// <summary>
    /// Converts this coroutine to an <see cref="IntoLuau"/> value without creating another tracked reference.
    /// </summary>
    /// <param name="value">The tracked coroutine reference.</param>
    /// <returns>A temporary representation of the same tracked coroutine.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked.</exception>
    public static implicit operator IntoLuau(LuauCoroutine value) => IntoLuau.Borrow(value._state, value._handle);

    /// <inheritdoc/>
    public LuauValue DisposeAndToLuauValue() => LuauValue.Move(_state, _handle, LuauValueType.Thread);

    /// <inheritdoc />
    public override string ToString() => Helpers.HandleToString(_state, _handle);

    /// <summary>
    /// Releases this coroutine reference from the state registry.
    /// </summary>
    /// <remarks> A resume in progress keeps the coroutine alive until it ends. </remarks>
    public void Dispose() => _state?.ReferenceTracker.ReleaseRef(_handle);

    private TResult ResumeCore<TResult>(scoped in RefEnumerable<IntoLuau> args, Func<LuauArgs, TResult> resultSelector)
    {
        _state.ThrowIfDisposed();
        return CoroutineDriver
            .StartResume(_state, _handle, args, allowsAwait: false, CancellationToken.None)
            .Run(resultSelector);
    }

    private ValueTask<TResult> ResumeCoreAsync<TResult>(
        scoped in RefEnumerable<IntoLuau> args,
        Func<LuauArgs, TResult> resultSelector,
        CancellationToken cancellationToken
    )
    {
        LuauState state = _state.GetTrackedReferenceOrThrow(_handle).ValidateInternal();
        LuauSynchronizationContext context = state.AsyncContext;
        if (context.TryEnter(out LuauSynchronizationContext.Turn turn))
        {
            using (turn)
            {
                return LuauSynchronizationContext.Detach(
                    CoroutineDriver
                        .StartResume(state, _handle, args, allowsAwait: true, cancellationToken)
                        .RunAsync(resultSelector, yieldIsError: false)
                );
            }
        }

        // Another thread executes the state: resume in a later turn, with copies of the arguments.
        return QueueResume(state, _handle, IntoLuau.CaptureBorrowed(args), resultSelector, cancellationToken);
    }

    // Separate from ResumeCoreAsync, so that only a queued resume allocates the closure.
    private static ValueTask<TResult> QueueResume<TResult>(
        LuauState state,
        ulong handle,
        IntoLuauCopied[] copiedArgs,
        Func<LuauArgs, TResult> resultSelector,
        CancellationToken cancellationToken
    )
    {
        ulong queuedHandle = state.ReferenceTracker.CountRefOrThrow(handle);
        try
        {
            return state.AsyncContext.Queue(() =>
            {
                try
                {
                    return CoroutineDriver
                        .StartResume(state, queuedHandle, copiedArgs, allowsAwait: true, cancellationToken)
                        .RunAsync(resultSelector, yieldIsError: false);
                }
                finally
                {
                    // StartResume takes its own root; the queued reference is no longer needed.
                    state.ReferenceTracker.ReleaseRef(queuedHandle);
                }
            });
        }
        catch
        {
            state.ReferenceTracker.ReleaseRef(queuedHandle);
            throw;
        }
    }
}
