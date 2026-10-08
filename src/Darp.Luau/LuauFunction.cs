using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Internal;
using Darp.Luau.Utils;

namespace Darp.Luau;

/// <summary>
/// Represents an owned Luau function reference stored in the registry.
/// </summary>
[SuppressMessage(
    "Performance",
    "CA1815:Override equals and operator equals on value types",
    Justification = "This wrapper is an ownership handle; custom value equality would imply Lua identity semantics the API does not guarantee."
)]
public readonly struct LuauFunction : ILuauReference
{
    private readonly LuauState? _state;
    private readonly ulong _handle;

    /// <summary>
    /// Gets whether this wrapper no longer points to a tracked Luau function reference.
    /// </summary>
    public bool IsDisposed => !_state.IsReferenceValid(_handle);

    /// <summary>
    /// Do not initialize directly. Create functions through <see cref="LuauState"/> APIs.
    /// </summary>
    [Obsolete("Do not initialize the LuauFunction. Create using the LuauState instead", true)]
    public LuauFunction() { }

    internal LuauFunction(LuauState? state, ulong handle)
    {
        _state = state;
        _handle = handle;
    }

    internal ulong GetHandleOrThrow()
    {
        _ = _state.GetTrackedReferenceOrThrow(_handle);
        return _handle;
    }

    /// <summary> Invokes the referenced function and ignores any return values. </summary>
    /// <param name="args">The arguments passed to the Luau function.</param>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked or the state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a call error.</exception>
    public void Invoke(params RefEnumerable<IntoLuau> args) =>
        LuauFunctionInvokeCore.Invoke(_state.GetTrackedReferenceOrThrow(_handle), args);

    /// <summary> Invokes the referenced function and converts the first return value. </summary>
    /// <param name="args">The arguments passed to the Luau function.</param>
    /// <typeparam name="TR">Managed return type to convert to.</typeparam>
    /// <returns> One single return value (additional values will be ignored) </returns>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked or the state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a call error.</exception>
    /// <exception cref="InvalidCastException">
    /// Thrown when the first Luau return value cannot be converted to <typeparamref name="TR"/>.
    /// </exception>
    public TR Invoke<TR>(params RefEnumerable<IntoLuau> args)
    {
        return LuauFunctionInvokeCore.Invoke(
            _state.GetTrackedReferenceOrThrow(_handle),
            args,
            LuauFunctionInvokeCore.ResultSelector<TR>
        );
    }

    /// <summary> Invokes the referenced function and converts the first two return values. </summary>
    /// <param name="args">The arguments passed to the Luau function.</param>
    /// <returns> Two return values (additional values will be ignored) </returns>
    /// <typeparam name="TR1">Managed return type to convert to.</typeparam>
    /// <typeparam name="TR2">Managed return type to convert to.</typeparam>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked or the state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a call error.</exception>
    /// <exception cref="InvalidCastException">
    /// Thrown when the Luau return values cannot be converted to <typeparamref name="TR1"/> or <typeparamref name="TR2"/>.
    /// </exception>
    public (TR1, TR2) Invoke<TR1, TR2>(params RefEnumerable<IntoLuau> args)
    {
        return LuauFunctionInvokeCore.Invoke(
            _state.GetTrackedReferenceOrThrow(_handle),
            args,
            LuauFunctionInvokeCore.ResultSelector<TR1, TR2>
        );
    }

    /// <summary> Invokes the referenced function and converts the first three return values. </summary>
    /// <param name="args">The arguments passed to the Luau function.</param>
    /// <returns> Three return values (additional values will be ignored) </returns>
    /// <typeparam name="TR1">Managed return type to convert to.</typeparam>
    /// <typeparam name="TR2">Managed return type to convert to.</typeparam>
    /// <typeparam name="TR3">Managed return type to convert to.</typeparam>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked or the state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a call error.</exception>
    /// <exception cref="InvalidCastException">
    /// Thrown when the Luau return values cannot be converted to <typeparamref name="TR1"/>, <typeparamref name="TR2"/>, or <typeparamref name="TR3"/>.
    /// </exception>
    public (TR1, TR2, TR3) Invoke<TR1, TR2, TR3>(params RefEnumerable<IntoLuau> args)
    {
        return LuauFunctionInvokeCore.Invoke(
            _state.GetTrackedReferenceOrThrow(_handle),
            args,
            LuauFunctionInvokeCore.ResultSelector<TR1, TR2, TR3>
        );
    }

    /// <summary> Invokes the referenced function and converts the first four return values. </summary>
    /// <param name="args">The arguments passed to the Luau function.</param>
    /// <returns> Four return values (additional values will be ignored) </returns>
    /// <typeparam name="TR1">Managed return type to convert to.</typeparam>
    /// <typeparam name="TR2">Managed return type to convert to.</typeparam>
    /// <typeparam name="TR3">Managed return type to convert to.</typeparam>
    /// <typeparam name="TR4">Managed return type to convert to.</typeparam>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked or the state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a call error.</exception>
    /// <exception cref="InvalidCastException">
    /// Thrown when the Luau return values cannot be converted to <typeparamref name="TR1"/>, <typeparamref name="TR2"/>, <typeparamref name="TR3"/>, or <typeparamref name="TR4"/>.
    /// </exception>
    public (TR1, TR2, TR3, TR4) Invoke<TR1, TR2, TR3, TR4>(params RefEnumerable<IntoLuau> args)
    {
        return LuauFunctionInvokeCore.Invoke(
            _state.GetTrackedReferenceOrThrow(_handle),
            args,
            LuauFunctionInvokeCore.ResultSelector<TR1, TR2, TR3, TR4>
        );
    }

    /// <summary> Invokes the referenced function and returns all Luau return values as raw <see cref="LuauValue"/> instances. </summary>
    /// <param name="args">The arguments passed to the Luau function.</param>
    /// <returns> All Luau return values as an array. </returns>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked or the state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a call error.</exception>
    public LuauValue[] InvokeMulti(params RefEnumerable<IntoLuau> args)
    {
        return LuauFunctionInvokeCore.Invoke(
            _state.GetTrackedReferenceOrThrow(_handle),
            args,
            LuauFunctionInvokeCore.ResultSelectorMulti
        );
    }

    /// <summary> Invokes the function asynchronously and ignores any return values. </summary>
    /// <param name="args">The arguments passed to the Luau function.</param>
    /// <returns>A task that completes when the function has returned.</returns>
    /// <remarks>
    /// The function runs on a new coroutine, so managed callbacks may use
    /// a <see cref="LuauAwaiter"/> to suspend it until their work completes.
    /// Without such a callback, the returned task is already completed.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked or the state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a call error, or when the function yields.</exception>
    public ValueTask InvokeAsync(params RefEnumerable<IntoLuau> args) => InvokeAsync(args, CancellationToken.None);

    /// <inheritdoc cref="InvokeAsync(RefEnumerable{IntoLuau})"/>
    /// <param name="args">The arguments passed to the Luau function.</param>
    /// <param name="cancellationToken">
    /// Stops the script at its next safepoint when cancelled. Available to managed callbacks as
    /// <see cref="LuauArgs.CancellationToken"/>.
    /// </param>
    /// <exception cref="OperationCanceledException">Thrown when the work of an awaiting managed callback is canceled.</exception>
    public ValueTask InvokeAsync(scoped RefEnumerable<IntoLuau> args, CancellationToken cancellationToken) =>
        LuauFunctionInvokeCore.WithoutResult(
            LuauFunctionInvokeCore.InvokeAsync(
                _state,
                _handle,
                args,
                LuauFunctionInvokeCore.IgnoreResults,
                cancellationToken
            )
        );

    /// <summary> Invokes the function asynchronously and converts the first return value. </summary>
    /// <param name="args">The arguments passed to the Luau function.</param>
    /// <typeparam name="TR">Managed return type to convert to.</typeparam>
    /// <returns>The first return value converted to <typeparamref name="TR"/>.</returns>
    /// <remarks>
    /// The function runs on a new coroutine, so managed callbacks may use
    /// a <see cref="LuauAwaiter"/> to suspend it until their work completes.
    /// Without such a callback, the returned task is already completed.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked or the state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a call error, or when the function yields.</exception>
    /// <exception cref="InvalidCastException">Thrown when a return value cannot be converted to the requested managed type.</exception>
    public ValueTask<TR> InvokeAsync<TR>(params RefEnumerable<IntoLuau> args) =>
        InvokeAsync<TR>(args, CancellationToken.None);

    /// <inheritdoc cref="InvokeAsync{TR}(RefEnumerable{IntoLuau})"/>
    /// <param name="args">The arguments passed to the Luau function.</param>
    /// <param name="cancellationToken">
    /// Stops the script at its next safepoint when cancelled. Available to managed callbacks as
    /// <see cref="LuauArgs.CancellationToken"/>.
    /// </param>
    /// <exception cref="OperationCanceledException">Thrown when the work of an awaiting managed callback is canceled.</exception>
    public ValueTask<TR> InvokeAsync<TR>(scoped RefEnumerable<IntoLuau> args, CancellationToken cancellationToken) =>
        LuauFunctionInvokeCore.InvokeAsync(
            _state,
            _handle,
            args,
            LuauFunctionInvokeCore.ResultSelector<TR>,
            cancellationToken
        );

    /// <summary> Invokes the function asynchronously and converts the first two return values. </summary>
    /// <param name="args">The arguments passed to the Luau function.</param>
    /// <typeparam name="TR1">Managed return type to convert to.</typeparam>
    /// <typeparam name="TR2">Managed return type to convert to.</typeparam>
    /// <returns>Two return values (additional values will be ignored).</returns>
    /// <remarks>
    /// The function runs on a new coroutine, so managed callbacks may use
    /// a <see cref="LuauAwaiter"/> to suspend it until their work completes.
    /// Without such a callback, the returned task is already completed.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked or the state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a call error, or when the function yields.</exception>
    /// <exception cref="InvalidCastException">Thrown when a return value cannot be converted to the requested managed type.</exception>
    public ValueTask<(TR1, TR2)> InvokeAsync<TR1, TR2>(params RefEnumerable<IntoLuau> args) =>
        InvokeAsync<TR1, TR2>(args, CancellationToken.None);

    /// <inheritdoc cref="InvokeAsync{TR1, TR2}(RefEnumerable{IntoLuau})"/>
    /// <param name="args">The arguments passed to the Luau function.</param>
    /// <param name="cancellationToken">
    /// Stops the script at its next safepoint when cancelled. Available to managed callbacks as
    /// <see cref="LuauArgs.CancellationToken"/>.
    /// </param>
    /// <exception cref="OperationCanceledException">Thrown when the work of an awaiting managed callback is canceled.</exception>
    public ValueTask<(TR1, TR2)> InvokeAsync<TR1, TR2>(
        scoped RefEnumerable<IntoLuau> args,
        CancellationToken cancellationToken
    ) =>
        LuauFunctionInvokeCore.InvokeAsync(
            _state,
            _handle,
            args,
            LuauFunctionInvokeCore.ResultSelector<TR1, TR2>,
            cancellationToken
        );

    /// <summary> Invokes the function asynchronously and converts the first three return values. </summary>
    /// <param name="args">The arguments passed to the Luau function.</param>
    /// <typeparam name="TR1">Managed return type to convert to.</typeparam>
    /// <typeparam name="TR2">Managed return type to convert to.</typeparam>
    /// <typeparam name="TR3">Managed return type to convert to.</typeparam>
    /// <returns>Three return values (additional values will be ignored).</returns>
    /// <remarks>
    /// The function runs on a new coroutine, so managed callbacks may use
    /// a <see cref="LuauAwaiter"/> to suspend it until their work completes.
    /// Without such a callback, the returned task is already completed.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked or the state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a call error, or when the function yields.</exception>
    /// <exception cref="InvalidCastException">Thrown when a return value cannot be converted to the requested managed type.</exception>
    public ValueTask<(TR1, TR2, TR3)> InvokeAsync<TR1, TR2, TR3>(params RefEnumerable<IntoLuau> args) =>
        InvokeAsync<TR1, TR2, TR3>(args, CancellationToken.None);

    /// <inheritdoc cref="InvokeAsync{TR1, TR2, TR3}(RefEnumerable{IntoLuau})"/>
    /// <param name="args">The arguments passed to the Luau function.</param>
    /// <param name="cancellationToken">
    /// Stops the script at its next safepoint when cancelled. Available to managed callbacks as
    /// <see cref="LuauArgs.CancellationToken"/>.
    /// </param>
    /// <exception cref="OperationCanceledException">Thrown when the work of an awaiting managed callback is canceled.</exception>
    public ValueTask<(TR1, TR2, TR3)> InvokeAsync<TR1, TR2, TR3>(
        scoped RefEnumerable<IntoLuau> args,
        CancellationToken cancellationToken
    ) =>
        LuauFunctionInvokeCore.InvokeAsync(
            _state,
            _handle,
            args,
            LuauFunctionInvokeCore.ResultSelector<TR1, TR2, TR3>,
            cancellationToken
        );

    /// <summary> Invokes the function asynchronously and converts the first four return values. </summary>
    /// <param name="args">The arguments passed to the Luau function.</param>
    /// <typeparam name="TR1">Managed return type to convert to.</typeparam>
    /// <typeparam name="TR2">Managed return type to convert to.</typeparam>
    /// <typeparam name="TR3">Managed return type to convert to.</typeparam>
    /// <typeparam name="TR4">Managed return type to convert to.</typeparam>
    /// <returns>Four return values (additional values will be ignored).</returns>
    /// <remarks>
    /// The function runs on a new coroutine, so managed callbacks may use
    /// a <see cref="LuauAwaiter"/> to suspend it until their work completes.
    /// Without such a callback, the returned task is already completed.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked or the state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a call error, or when the function yields.</exception>
    /// <exception cref="InvalidCastException">Thrown when a return value cannot be converted to the requested managed type.</exception>
    public ValueTask<(TR1, TR2, TR3, TR4)> InvokeAsync<TR1, TR2, TR3, TR4>(params RefEnumerable<IntoLuau> args) =>
        InvokeAsync<TR1, TR2, TR3, TR4>(args, CancellationToken.None);

    /// <inheritdoc cref="InvokeAsync{TR1, TR2, TR3, TR4}(RefEnumerable{IntoLuau})"/>
    /// <param name="args">The arguments passed to the Luau function.</param>
    /// <param name="cancellationToken">
    /// Stops the script at its next safepoint when cancelled. Available to managed callbacks as
    /// <see cref="LuauArgs.CancellationToken"/>.
    /// </param>
    /// <exception cref="OperationCanceledException">Thrown when the work of an awaiting managed callback is canceled.</exception>
    public ValueTask<(TR1, TR2, TR3, TR4)> InvokeAsync<TR1, TR2, TR3, TR4>(
        scoped RefEnumerable<IntoLuau> args,
        CancellationToken cancellationToken
    ) =>
        LuauFunctionInvokeCore.InvokeAsync(
            _state,
            _handle,
            args,
            LuauFunctionInvokeCore.ResultSelector<TR1, TR2, TR3, TR4>,
            cancellationToken
        );

    /// <summary> Invokes the function asynchronously and returns all Luau return values as raw <see cref="LuauValue"/> instances. </summary>
    /// <param name="args">The arguments passed to the Luau function.</param>
    /// <returns>All Luau return values as an array.</returns>
    /// <remarks>
    /// The function runs on a new coroutine, so managed callbacks may use
    /// a <see cref="LuauAwaiter"/> to suspend it until their work completes.
    /// Without such a callback, the returned task is already completed.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked or the state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a call error, or when the function yields.</exception>
    public ValueTask<LuauValue[]> InvokeMultiAsync(params RefEnumerable<IntoLuau> args) =>
        InvokeMultiAsync(args, CancellationToken.None);

    /// <inheritdoc cref="InvokeMultiAsync(RefEnumerable{IntoLuau})"/>
    /// <param name="args">The arguments passed to the Luau function.</param>
    /// <param name="cancellationToken">
    /// Stops the script at its next safepoint when cancelled. Available to managed callbacks as
    /// <see cref="LuauArgs.CancellationToken"/>.
    /// </param>
    /// <exception cref="OperationCanceledException">Thrown when the work of an awaiting managed callback is canceled.</exception>
    public ValueTask<LuauValue[]> InvokeMultiAsync(
        scoped RefEnumerable<IntoLuau> args,
        CancellationToken cancellationToken
    ) =>
        LuauFunctionInvokeCore.InvokeAsync(
            _state,
            _handle,
            args,
            LuauFunctionInvokeCore.ResultSelectorMulti,
            cancellationToken
        );

    /// <summary>
    /// Converts this function to an <see cref="IntoLuau"/> value without creating another tracked reference.
    /// </summary>
    /// <param name="value">The tracked function reference.</param>
    /// <returns>A temporary representation of the same tracked function.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked.</exception>
    public static implicit operator IntoLuau(LuauFunction value) => IntoLuau.Borrow(value._state, value._handle);

    /// <inheritdoc/>
    public LuauValue DisposeAndToLuauValue() => LuauValue.Move(_state, _handle, LuauValueType.Function);

    /// <inheritdoc />
    public override string ToString() => Helpers.HandleToString(_state, _handle);

    /// <summary>
    /// Releases this function reference from the state registry.
    /// </summary>
    public void Dispose() => _state?.ReferenceTracker.ReleaseRef(_handle);
}
