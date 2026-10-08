using Darp.Luau.Internal;
using Darp.Luau.Native;
using Darp.Luau.Utils;

namespace Darp.Luau;

/// <summary>
/// Represents a borrowed, stack-bound Luau function value read from callback arguments.
/// </summary>
/// <remarks>
/// This view does not own a registry reference.
/// It is valid only while the originating callback frame is active on the same <see cref="LuauState"/>.
/// Using it after the callback frame ends throws <see cref="ObjectDisposedException"/>.
/// </remarks>
public readonly ref struct LuauFunctionView : ILuauView<LuauFunction>
{
    private readonly StackReference _reference;

    internal unsafe LuauFunctionView(LuauState state, lua_State* luaState, int stackIndex) =>
        _reference = new StackReference(state, luaState, stackIndex);

    /// <summary> Invokes the borrowed function with arguments and ignores any return values. </summary>
    /// <exception cref="ObjectDisposedException">Thrown when the callback frame has ended or the state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a call error.</exception>
    public void Invoke(params RefEnumerable<IntoLuau> args) => LuauFunctionInvokeCore.Invoke(_reference, args);

    /// <summary> Invokes the borrowed function with arguments and converts the first return value. </summary>
    /// <param name="args">The arguments passed to the Luau function.</param>
    /// <typeparam name="TR">Managed return type to convert to.</typeparam>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked or the state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a call error.</exception>
    /// <exception cref="InvalidCastException">
    /// Thrown when the first Luau return value cannot be converted to <typeparamref name="TR"/>.
    /// </exception>
    public TR Invoke<TR>(params RefEnumerable<IntoLuau> args)
    {
        return LuauFunctionInvokeCore.Invoke(_reference, args, LuauFunctionInvokeCore.ResultSelector<TR>);
    }

    /// <summary> Invokes the borrowed function with arguments and converts the first two return values. </summary>
    /// <param name="args">The arguments passed to the Luau function.</param>
    /// <typeparam name="TR1">Managed return type to convert to.</typeparam>
    /// <typeparam name="TR2">Managed return type to convert to.</typeparam>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked or the state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a call error.</exception>
    /// <exception cref="InvalidCastException">
    /// Thrown when the Luau return values cannot be converted to <typeparamref name="TR1"/> or <typeparamref name="TR2"/>.
    /// </exception>
    public (TR1, TR2) Invoke<TR1, TR2>(params RefEnumerable<IntoLuau> args)
    {
        return LuauFunctionInvokeCore.Invoke(_reference, args, LuauFunctionInvokeCore.ResultSelector<TR1, TR2>);
    }

    /// <summary> Invokes the borrowed function with arguments and converts the first three return values. </summary>
    /// <param name="args">The arguments passed to the Luau function.</param>
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
        return LuauFunctionInvokeCore.Invoke(_reference, args, LuauFunctionInvokeCore.ResultSelector<TR1, TR2, TR3>);
    }

    /// <summary> Invokes the borrowed function with arguments and converts the first four return values. </summary>
    /// <param name="args">The arguments passed to the Luau function.</param>
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
            _reference,
            args,
            LuauFunctionInvokeCore.ResultSelector<TR1, TR2, TR3, TR4>
        );
    }

    /// <summary> Invokes the borrowed function with arguments and returns all Luau return values as raw <see cref="LuauValue"/> instances. </summary>
    /// <param name="args">The arguments passed to the Luau function.</param>
    /// <exception cref="ObjectDisposedException">Thrown when this reference is no longer tracked or the state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a call error.</exception>
    public LuauValue[] InvokeMulti(params RefEnumerable<IntoLuau> args)
    {
        return LuauFunctionInvokeCore.Invoke(_reference, args, LuauFunctionInvokeCore.ResultSelectorMulti);
    }

    /// <summary> Invokes the function asynchronously and ignores any return values. </summary>
    /// <param name="args">The arguments passed to the Luau function.</param>
    /// <returns>A task that completes when the function has returned.</returns>
    /// <remarks>
    /// The function runs on a new coroutine, so managed callbacks may use
    /// a <see cref="LuauAwaiter"/> to suspend it until their work completes.
    /// Without such a callback, the returned task is already completed.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when the callback frame has ended or the state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a call error, or when the function yields.</exception>
    public ValueTask InvokeAsync(params RefEnumerable<IntoLuau> args) => InvokeAsync(args, CancellationToken.None);

    /// <inheritdoc cref="InvokeAsync(RefEnumerable{IntoLuau})"/>
    /// <param name="args">The arguments passed to the Luau function.</param>
    /// <param name="cancellationToken">
    /// Available to managed callbacks as <see cref="LuauArgs.CancellationToken"/>. Cancellation is cooperative.
    /// </param>
    /// <exception cref="OperationCanceledException">Thrown when the work of an awaiting managed callback is canceled.</exception>
    public ValueTask InvokeAsync(scoped RefEnumerable<IntoLuau> args, CancellationToken cancellationToken) =>
        LuauFunctionInvokeCore.WithoutResult(
            LuauFunctionInvokeCore.InvokeAsync(
                _reference,
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
    /// <exception cref="ObjectDisposedException">Thrown when the callback frame has ended or the state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a call error, or when the function yields.</exception>
    /// <exception cref="InvalidCastException">Thrown when a return value cannot be converted to the requested managed type.</exception>
    public ValueTask<TR> InvokeAsync<TR>(params RefEnumerable<IntoLuau> args) =>
        InvokeAsync<TR>(args, CancellationToken.None);

    /// <inheritdoc cref="InvokeAsync{TR}(RefEnumerable{IntoLuau})"/>
    /// <param name="args">The arguments passed to the Luau function.</param>
    /// <param name="cancellationToken">
    /// Available to managed callbacks as <see cref="LuauArgs.CancellationToken"/>. Cancellation is cooperative.
    /// </param>
    /// <exception cref="OperationCanceledException">Thrown when the work of an awaiting managed callback is canceled.</exception>
    public ValueTask<TR> InvokeAsync<TR>(scoped RefEnumerable<IntoLuau> args, CancellationToken cancellationToken) =>
        LuauFunctionInvokeCore.InvokeAsync(
            _reference,
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
    /// <exception cref="ObjectDisposedException">Thrown when the callback frame has ended or the state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a call error, or when the function yields.</exception>
    /// <exception cref="InvalidCastException">Thrown when a return value cannot be converted to the requested managed type.</exception>
    public ValueTask<(TR1, TR2)> InvokeAsync<TR1, TR2>(params RefEnumerable<IntoLuau> args) =>
        InvokeAsync<TR1, TR2>(args, CancellationToken.None);

    /// <inheritdoc cref="InvokeAsync{TR1, TR2}(RefEnumerable{IntoLuau})"/>
    /// <param name="args">The arguments passed to the Luau function.</param>
    /// <param name="cancellationToken">
    /// Available to managed callbacks as <see cref="LuauArgs.CancellationToken"/>. Cancellation is cooperative.
    /// </param>
    /// <exception cref="OperationCanceledException">Thrown when the work of an awaiting managed callback is canceled.</exception>
    public ValueTask<(TR1, TR2)> InvokeAsync<TR1, TR2>(
        scoped RefEnumerable<IntoLuau> args,
        CancellationToken cancellationToken
    ) =>
        LuauFunctionInvokeCore.InvokeAsync(
            _reference,
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
    /// <exception cref="ObjectDisposedException">Thrown when the callback frame has ended or the state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a call error, or when the function yields.</exception>
    /// <exception cref="InvalidCastException">Thrown when a return value cannot be converted to the requested managed type.</exception>
    public ValueTask<(TR1, TR2, TR3)> InvokeAsync<TR1, TR2, TR3>(params RefEnumerable<IntoLuau> args) =>
        InvokeAsync<TR1, TR2, TR3>(args, CancellationToken.None);

    /// <inheritdoc cref="InvokeAsync{TR1, TR2, TR3}(RefEnumerable{IntoLuau})"/>
    /// <param name="args">The arguments passed to the Luau function.</param>
    /// <param name="cancellationToken">
    /// Available to managed callbacks as <see cref="LuauArgs.CancellationToken"/>. Cancellation is cooperative.
    /// </param>
    /// <exception cref="OperationCanceledException">Thrown when the work of an awaiting managed callback is canceled.</exception>
    public ValueTask<(TR1, TR2, TR3)> InvokeAsync<TR1, TR2, TR3>(
        scoped RefEnumerable<IntoLuau> args,
        CancellationToken cancellationToken
    ) =>
        LuauFunctionInvokeCore.InvokeAsync(
            _reference,
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
    /// <exception cref="ObjectDisposedException">Thrown when the callback frame has ended or the state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a call error, or when the function yields.</exception>
    /// <exception cref="InvalidCastException">Thrown when a return value cannot be converted to the requested managed type.</exception>
    public ValueTask<(TR1, TR2, TR3, TR4)> InvokeAsync<TR1, TR2, TR3, TR4>(params RefEnumerable<IntoLuau> args) =>
        InvokeAsync<TR1, TR2, TR3, TR4>(args, CancellationToken.None);

    /// <inheritdoc cref="InvokeAsync{TR1, TR2, TR3, TR4}(RefEnumerable{IntoLuau})"/>
    /// <param name="args">The arguments passed to the Luau function.</param>
    /// <param name="cancellationToken">
    /// Available to managed callbacks as <see cref="LuauArgs.CancellationToken"/>. Cancellation is cooperative.
    /// </param>
    /// <exception cref="OperationCanceledException">Thrown when the work of an awaiting managed callback is canceled.</exception>
    public ValueTask<(TR1, TR2, TR3, TR4)> InvokeAsync<TR1, TR2, TR3, TR4>(
        scoped RefEnumerable<IntoLuau> args,
        CancellationToken cancellationToken
    ) =>
        LuauFunctionInvokeCore.InvokeAsync(
            _reference,
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
    /// <exception cref="ObjectDisposedException">Thrown when the callback frame has ended or the state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a call error, or when the function yields.</exception>
    public ValueTask<LuauValue[]> InvokeMultiAsync(params RefEnumerable<IntoLuau> args) =>
        InvokeMultiAsync(args, CancellationToken.None);

    /// <inheritdoc cref="InvokeMultiAsync(RefEnumerable{IntoLuau})"/>
    /// <param name="args">The arguments passed to the Luau function.</param>
    /// <param name="cancellationToken">
    /// Available to managed callbacks as <see cref="LuauArgs.CancellationToken"/>. Cancellation is cooperative.
    /// </param>
    /// <exception cref="OperationCanceledException">Thrown when the work of an awaiting managed callback is canceled.</exception>
    public ValueTask<LuauValue[]> InvokeMultiAsync(
        scoped RefEnumerable<IntoLuau> args,
        CancellationToken cancellationToken
    ) =>
        LuauFunctionInvokeCore.InvokeAsync(
            _reference,
            args,
            LuauFunctionInvokeCore.ResultSelectorMulti,
            cancellationToken
        );

    /// <inheritdoc/>
    public LuauFunction ToOwned()
    {
        LuauState state = _reference.ValidateInternal();
        return new LuauFunction(state, ReferenceSourceExtensions.ToOwnedHandle(_reference));
    }

    /// <summary>
    /// Converts this borrowed function view to an <see cref="IntoLuau"/> value without creating an owned reference.
    /// </summary>
    /// <param name="value">The borrowed function view.</param>
    /// <returns>A temporary representation with the same callback-frame lifetime constraints.</returns>
    public static implicit operator IntoLuau(LuauFunctionView value) => IntoLuau.FromRefSource(value._reference);

    /// <inheritdoc />
    public override string ToString() => _reference.ToString();
}
