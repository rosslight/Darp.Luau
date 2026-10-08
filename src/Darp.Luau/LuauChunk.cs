using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using Darp.Luau.Internal;
using Darp.Luau.Native;
using Darp.Luau.Utils;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau;

internal enum LuauChunkSourceKind : byte
{
    Chars,
    Utf8Bytes,
}

/// <summary>
/// Compiler settings used when compiling a Luau chunk from source.
/// </summary>
/// <param name="OptimizationLevel">The Luau compiler optimization level.</param>
/// <param name="DebugLevel">The Luau compiler debug information level.</param>
internal sealed record LuauCompiler(int OptimizationLevel, int DebugLevel)
{
    /// <summary>
    /// The default compiler configuration used by chunk loading APIs.
    /// </summary>
    public static readonly LuauCompiler Default = new(OptimizationLevel: 1, DebugLevel: 1);
}

/// <summary>
/// Represents a lightweight executable Luau chunk loaded from source text.
/// </summary>
/// <remarks>
/// This type is an ephemeral wrapper over source text. Executing it recompiles the source each time.
/// Use <see cref="ToFunction"/> to compile and load the chunk as a reusable <see cref="LuauFunction"/>.
/// </remarks>
public readonly ref struct LuauChunk
{
    private const string DefaultChunkName = "=main";
    private const int LuaMultRet = -1;

    private readonly LuauState? _state;
    private readonly LuauCompiler _compiler;
    private readonly LuauChunkSourceKind _sourceKind;
    private readonly ReadOnlySpan<char> _charSource;
    private readonly ReadOnlySpan<byte> _utf8Source;
    private readonly ReadOnlySpan<char> _chunkName;
    private readonly ulong _environmentHandle;

    internal LuauChunk(LuauState? state, LuauCompiler compiler, ReadOnlySpan<char> source)
        : this(state, compiler, LuauChunkSourceKind.Chars, source, default, DefaultChunkName, environmentHandle: 0) { }

    internal LuauChunk(LuauState? state, LuauCompiler compiler, ReadOnlySpan<byte> source)
        : this(state, compiler, LuauChunkSourceKind.Utf8Bytes, default, source, DefaultChunkName, environmentHandle: 0)
    { }

    private LuauChunk(
        LuauState? state,
        LuauCompiler compiler,
        LuauChunkSourceKind sourceKind,
        ReadOnlySpan<char> charSource,
        ReadOnlySpan<byte> utf8Source,
        ReadOnlySpan<char> chunkName,
        ulong environmentHandle
    )
    {
        _state = state;
        _compiler = compiler;
        _sourceKind = sourceKind;
        _charSource = charSource;
        _utf8Source = utf8Source;
        _chunkName = chunkName;
        _environmentHandle = environmentHandle;
    }

    /// <summary> Returns a copy of this chunk configured with a specific chunk name. </summary>
    /// <param name="chunkName">The chunk name to use when loading the compiled bytecode.</param>
    /// <returns>A chunk configured with the provided chunk name.</returns>
    public LuauChunk WithName(ReadOnlySpan<char> chunkName) =>
        new(_state, _compiler, _sourceKind, _charSource, _utf8Source, chunkName, _environmentHandle);

    /// <summary> Returns a copy of this chunk configured with a dedicated execution environment. </summary>
    /// <param name="environment">The environment table to use for global lookups and assignments.</param>
    /// <returns>A chunk configured with the provided environment.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the owning state or environment is disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the environment belongs to a different state.</exception>
    public LuauChunk WithEnvironment(LuauTable environment)
    {
        LuauState state = GetState();
        LuauState environmentState = environment.GetStateOrThrow();
        if (!ReferenceEquals(state, environmentState))
            throw new InvalidOperationException("Chunk environment must belong to the same LuauState.");

        ulong environmentHandle = environment.GetHandleOrThrow();
        return new LuauChunk(_state, _compiler, _sourceKind, _charSource, _utf8Source, _chunkName, environmentHandle);
    }

    /// <summary> Compiles and executes the chunk, ignoring any return values. </summary>
    /// <exception cref="ObjectDisposedException">Thrown when the owning state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a load or runtime error.</exception>
    public void Execute(params RefEnumerable<IntoLuau> args) => ExecuteCore(args, nResults: 0);

    /// <summary> Compiles and executes the chunk and converts the first return value. </summary>
    /// <param name="args">The arguments passed to the chunk.</param>
    /// <typeparam name="TR">Managed return type to convert to.</typeparam>
    /// <returns>The first Luau return value converted to <typeparamref name="TR"/>.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the owning state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a load or runtime error.</exception>
    /// <exception cref="InvalidCastException">Thrown when the return value cannot be converted to <typeparamref name="TR"/>.</exception>
    public TR Execute<TR>(params RefEnumerable<IntoLuau> args) =>
        ExecuteCore(args, nResults: 1, LuauFunctionInvokeCore.ResultSelector<TR>);

    /// <summary> Compiles and executes the chunk and converts the first two return values. </summary>
    /// <param name="args">The arguments passed to the chunk.</param>
    /// <typeparam name="TR1">Managed return type to convert to.</typeparam>
    /// <typeparam name="TR2">Managed return type to convert to.</typeparam>
    /// <returns>The first two Luau return values converted to a tuple.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the owning state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a load or runtime error.</exception>
    /// <exception cref="InvalidCastException">Thrown when a return value cannot be converted to the requested managed type.</exception>
    public (TR1, TR2) Execute<TR1, TR2>(params RefEnumerable<IntoLuau> args) =>
        ExecuteCore(args, nResults: 2, LuauFunctionInvokeCore.ResultSelector<TR1, TR2>);

    /// <summary> Compiles and executes the chunk and converts the first three return values. </summary>
    /// <param name="args">The arguments passed to the chunk.</param>
    /// <typeparam name="TR1">Managed return type to convert to.</typeparam>
    /// <typeparam name="TR2">Managed return type to convert to.</typeparam>
    /// <typeparam name="TR3">Managed return type to convert to.</typeparam>
    /// <returns>The first three Luau return values converted to a tuple.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the owning state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a load or runtime error.</exception>
    /// <exception cref="InvalidCastException">Thrown when a return value cannot be converted to the requested managed type.</exception>
    public (TR1, TR2, TR3) Execute<TR1, TR2, TR3>(params RefEnumerable<IntoLuau> args) =>
        ExecuteCore(args, nResults: 3, LuauFunctionInvokeCore.ResultSelector<TR1, TR2, TR3>);

    /// <summary> Compiles and executes the chunk and converts the first four return values. </summary>
    /// <param name="args">The arguments passed to the chunk.</param>
    /// <typeparam name="TR1">Managed return type to convert to.</typeparam>
    /// <typeparam name="TR2">Managed return type to convert to.</typeparam>
    /// <typeparam name="TR3">Managed return type to convert to.</typeparam>
    /// <typeparam name="TR4">Managed return type to convert to.</typeparam>
    /// <returns>The first four Luau return values converted to a tuple.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the owning state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a load or runtime error.</exception>
    /// <exception cref="InvalidCastException">Thrown when a return value cannot be converted to the requested managed type.</exception>
    public (TR1, TR2, TR3, TR4) Execute<TR1, TR2, TR3, TR4>(params RefEnumerable<IntoLuau> args) =>
        ExecuteCore(args, nResults: 4, LuauFunctionInvokeCore.ResultSelector<TR1, TR2, TR3, TR4>);

    /// <summary>
    /// Compiles and executes the chunk and returns all Luau return values as raw <see cref="LuauValue"/> instances.
    /// </summary>
    /// <param name="args">The arguments passed to the chunk.</param>
    /// <returns>All Luau return values as an array.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the owning state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a load or runtime error.</exception>
    public LuauValue[] ExecuteMulti(params RefEnumerable<IntoLuau> args) =>
        ExecuteCore(args, nResults: LuaMultRet, LuauFunctionInvokeCore.ResultSelectorMulti);

    /// <summary> Compiles and executes the chunk on a coroutine, ignoring any return values. </summary>
    /// <param name="args">The arguments passed to the chunk.</param>
    /// <returns>A task that completes when the chunk has finished.</returns>
    /// <remarks>
    /// Managed callbacks may use a <see cref="LuauAwaiter"/> to suspend the chunk until
    /// their work completes. Without such a callback, the returned task is already completed.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when the owning state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a load or runtime error, or when the chunk yields.</exception>
    public ValueTask ExecuteAsync(params RefEnumerable<IntoLuau> args) => ExecuteAsync(args, CancellationToken.None);

    /// <inheritdoc cref="ExecuteAsync(RefEnumerable{IntoLuau})"/>
    /// <param name="args">The arguments passed to the chunk.</param>
    /// <param name="cancellationToken">
    /// Available to managed callbacks as <see cref="LuauArgs.CancellationToken"/>. Cancellation is cooperative.
    /// </param>
    /// <exception cref="OperationCanceledException">Thrown when the work of an awaiting managed callback is canceled.</exception>
    public ValueTask ExecuteAsync(scoped RefEnumerable<IntoLuau> args, CancellationToken cancellationToken) =>
        LuauFunctionInvokeCore.WithoutResult(
            ExecuteCoreAsync(args, nResults: 0, LuauFunctionInvokeCore.IgnoreResults, cancellationToken)
        );

    /// <summary> Compiles and executes the chunk on a coroutine and converts the first return value. </summary>
    /// <param name="args">The arguments passed to the chunk.</param>
    /// <typeparam name="TR">Managed return type to convert to.</typeparam>
    /// <returns>The first Luau return value converted to <typeparamref name="TR"/>.</returns>
    /// <remarks>
    /// Managed callbacks may use a <see cref="LuauAwaiter"/> to suspend the chunk until
    /// their work completes. Without such a callback, the returned task is already completed.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Thrown when the owning state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a load or runtime error, or when the chunk yields.</exception>
    /// <exception cref="InvalidCastException">Thrown when the return value cannot be converted to <typeparamref name="TR"/>.</exception>
    public ValueTask<TR> ExecuteAsync<TR>(params RefEnumerable<IntoLuau> args) =>
        ExecuteAsync<TR>(args, CancellationToken.None);

    /// <inheritdoc cref="ExecuteAsync{TR}(RefEnumerable{IntoLuau})"/>
    /// <param name="args">The arguments passed to the chunk.</param>
    /// <param name="cancellationToken">
    /// Available to managed callbacks as <see cref="LuauArgs.CancellationToken"/>. Cancellation is cooperative.
    /// </param>
    /// <exception cref="OperationCanceledException">Thrown when the work of an awaiting managed callback is canceled.</exception>
    public ValueTask<TR> ExecuteAsync<TR>(scoped RefEnumerable<IntoLuau> args, CancellationToken cancellationToken) =>
        ExecuteCoreAsync(args, nResults: 1, LuauFunctionInvokeCore.ResultSelector<TR>, cancellationToken);

    /// <summary> Compiles and executes the chunk on a coroutine and converts the first two return values. </summary>
    /// <param name="args">The arguments passed to the chunk.</param>
    /// <typeparam name="TR1">Managed return type to convert to.</typeparam>
    /// <typeparam name="TR2">Managed return type to convert to.</typeparam>
    /// <returns>The first two Luau return values converted to a tuple.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the owning state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a load or runtime error, or when the chunk yields.</exception>
    /// <exception cref="InvalidCastException">Thrown when a return value cannot be converted to the requested managed type.</exception>
    public ValueTask<(TR1, TR2)> ExecuteAsync<TR1, TR2>(params RefEnumerable<IntoLuau> args) =>
        ExecuteAsync<TR1, TR2>(args, CancellationToken.None);

    /// <inheritdoc cref="ExecuteAsync{TR1, TR2}(RefEnumerable{IntoLuau})"/>
    /// <param name="args">The arguments passed to the chunk.</param>
    /// <param name="cancellationToken">
    /// Available to managed callbacks as <see cref="LuauArgs.CancellationToken"/>. Cancellation is cooperative.
    /// </param>
    /// <exception cref="OperationCanceledException">Thrown when the work of an awaiting managed callback is canceled.</exception>
    public ValueTask<(TR1, TR2)> ExecuteAsync<TR1, TR2>(
        scoped RefEnumerable<IntoLuau> args,
        CancellationToken cancellationToken
    ) => ExecuteCoreAsync(args, nResults: 2, LuauFunctionInvokeCore.ResultSelector<TR1, TR2>, cancellationToken);

    /// <summary> Compiles and executes the chunk on a coroutine and converts the first three return values. </summary>
    /// <param name="args">The arguments passed to the chunk.</param>
    /// <typeparam name="TR1">Managed return type to convert to.</typeparam>
    /// <typeparam name="TR2">Managed return type to convert to.</typeparam>
    /// <typeparam name="TR3">Managed return type to convert to.</typeparam>
    /// <returns>The first three Luau return values converted to a tuple.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the owning state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a load or runtime error, or when the chunk yields.</exception>
    /// <exception cref="InvalidCastException">Thrown when a return value cannot be converted to the requested managed type.</exception>
    public ValueTask<(TR1, TR2, TR3)> ExecuteAsync<TR1, TR2, TR3>(params RefEnumerable<IntoLuau> args) =>
        ExecuteAsync<TR1, TR2, TR3>(args, CancellationToken.None);

    /// <inheritdoc cref="ExecuteAsync{TR1, TR2, TR3}(RefEnumerable{IntoLuau})"/>
    /// <param name="args">The arguments passed to the chunk.</param>
    /// <param name="cancellationToken">
    /// Available to managed callbacks as <see cref="LuauArgs.CancellationToken"/>. Cancellation is cooperative.
    /// </param>
    /// <exception cref="OperationCanceledException">Thrown when the work of an awaiting managed callback is canceled.</exception>
    public ValueTask<(TR1, TR2, TR3)> ExecuteAsync<TR1, TR2, TR3>(
        scoped RefEnumerable<IntoLuau> args,
        CancellationToken cancellationToken
    ) => ExecuteCoreAsync(args, nResults: 3, LuauFunctionInvokeCore.ResultSelector<TR1, TR2, TR3>, cancellationToken);

    /// <summary> Compiles and executes the chunk on a coroutine and converts the first four return values. </summary>
    /// <param name="args">The arguments passed to the chunk.</param>
    /// <typeparam name="TR1">Managed return type to convert to.</typeparam>
    /// <typeparam name="TR2">Managed return type to convert to.</typeparam>
    /// <typeparam name="TR3">Managed return type to convert to.</typeparam>
    /// <typeparam name="TR4">Managed return type to convert to.</typeparam>
    /// <returns>The first four Luau return values converted to a tuple.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the owning state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a load or runtime error, or when the chunk yields.</exception>
    /// <exception cref="InvalidCastException">Thrown when a return value cannot be converted to the requested managed type.</exception>
    public ValueTask<(TR1, TR2, TR3, TR4)> ExecuteAsync<TR1, TR2, TR3, TR4>(params RefEnumerable<IntoLuau> args) =>
        ExecuteAsync<TR1, TR2, TR3, TR4>(args, CancellationToken.None);

    /// <inheritdoc cref="ExecuteAsync{TR1, TR2, TR3, TR4}(RefEnumerable{IntoLuau})"/>
    /// <param name="args">The arguments passed to the chunk.</param>
    /// <param name="cancellationToken">
    /// Available to managed callbacks as <see cref="LuauArgs.CancellationToken"/>. Cancellation is cooperative.
    /// </param>
    /// <exception cref="OperationCanceledException">Thrown when the work of an awaiting managed callback is canceled.</exception>
    public ValueTask<(TR1, TR2, TR3, TR4)> ExecuteAsync<TR1, TR2, TR3, TR4>(
        scoped RefEnumerable<IntoLuau> args,
        CancellationToken cancellationToken
    ) =>
        ExecuteCoreAsync(
            args,
            nResults: 4,
            LuauFunctionInvokeCore.ResultSelector<TR1, TR2, TR3, TR4>,
            cancellationToken
        );

    /// <summary>
    /// Compiles and executes the chunk on a coroutine and returns all Luau return values as raw
    /// <see cref="LuauValue"/> instances.
    /// </summary>
    /// <param name="args">The arguments passed to the chunk.</param>
    /// <returns>All Luau return values as an array.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the owning state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a load or runtime error, or when the chunk yields.</exception>
    public ValueTask<LuauValue[]> ExecuteMultiAsync(params RefEnumerable<IntoLuau> args) =>
        ExecuteMultiAsync(args, CancellationToken.None);

    /// <inheritdoc cref="ExecuteMultiAsync(RefEnumerable{IntoLuau})"/>
    /// <param name="args">The arguments passed to the chunk.</param>
    /// <param name="cancellationToken">
    /// Available to managed callbacks as <see cref="LuauArgs.CancellationToken"/>. Cancellation is cooperative.
    /// </param>
    /// <exception cref="OperationCanceledException">Thrown when the work of an awaiting managed callback is canceled.</exception>
    public ValueTask<LuauValue[]> ExecuteMultiAsync(
        scoped RefEnumerable<IntoLuau> args,
        CancellationToken cancellationToken
    ) => ExecuteCoreAsync(args, nResults: 0, LuauFunctionInvokeCore.ResultSelectorMulti, cancellationToken);

    /// <summary> Compiles and loads the chunk as a reusable <see cref="LuauFunction"/>. </summary>
    /// <returns>The loaded chunk represented as a function.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the owning state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a load error.</exception>
    public unsafe LuauFunction ToFunction()
    {
        LuauState state = GetState();
        lua_State* L = state.L;
#if DEBUG
        using var guard = new StackGuard(L, expectedDelta: 0);
#endif
        LoadCompiledChunk(L);
        ulong reference = state.ReferenceTracker.TrackAndPopRef(L, -1);
        return new LuauFunction(state, reference);
    }

    private unsafe TResult ExecuteCore<TResult>(
        scoped RefEnumerable<IntoLuau> args,
        int nResults,
        Func<LuauArgs, TResult> resultSelector
    )
    {
        LuauState state = GetState();
        lua_State* L = state.L;
#if DEBUG
        using var guard = new StackGuard(L, expectedDelta: 0);
#endif
        int topBeforeInvoke = lua_gettop(L);
        try
        {
            LoadCompiledChunk(L);
            int nArgs = args.Length;
            for (int i = 0; i < nArgs; i++)
                args[i].Push(state);

            int status = lua_pcall(L, nArgs, nResults, 0);
            LuaException.ThrowIfNotOk(L, status, "lua_pcall");
            var result = new LuauArgs(state, lua_gettop(L) - topBeforeInvoke, topBeforeInvoke + 1);
            return resultSelector(result);
        }
        finally
        {
            lua_settop(L, topBeforeInvoke);
        }
    }

    private unsafe void ExecuteCore(scoped RefEnumerable<IntoLuau> args, int nResults)
    {
        LuauState state = GetState();
        lua_State* L = state.L;
#if DEBUG
        using var guard = new StackGuard(L, expectedDelta: 0);
#endif
        int topBeforeInvoke = lua_gettop(L);
        try
        {
            LoadCompiledChunk(L);
            int nArgs = args.Length;
            for (int i = 0; i < nArgs; i++)
                args[i].Push(state);

            int status = lua_pcall(L, nArgs, nResults, 0);
            LuaException.ThrowIfNotOk(L, status, "lua_pcall");
        }
        finally
        {
            lua_settop(L, topBeforeInvoke);
        }
    }

    /// <param name="args">The arguments passed to the chunk.</param>
    /// <param name="nResults">Missing results up to this count are read as <c>nil</c>, like the sync overloads.</param>
    /// <param name="resultSelector">Reads the results.</param>
    /// <param name="cancellationToken">Passed to the work of awaiting managed callbacks.</param>
    private unsafe ValueTask<TResult> ExecuteCoreAsync<TResult>(
        scoped RefEnumerable<IntoLuau> args,
        int nResults,
        Func<LuauArgs, TResult> resultSelector,
        CancellationToken cancellationToken
    )
    {
        LuauState state = GetState();
        LuauSynchronizationContext context = state.AsyncContext;
        if (context.TryEnter(out LuauSynchronizationContext.Turn turn))
        {
            using (turn)
            {
#if DEBUG
                using var guard = new StackGuard(state.L, expectedDelta: 0);
#endif
                // The chunk is loaded on the main stack and moved onto the coroutine, so the environment is applied
                // exactly like for Execute.
                LoadCompiledChunk(state.L);
                return LuauSynchronizationContext.Detach(
                    CoroutineDriver
                        .StartInvocation(state, args, minResultCount: nResults, cancellationToken)
                        .RunAsync(resultSelector, yieldIsError: true)
                );
            }
        }

        // Another thread executes the state: execute in a later turn, with copies of the source and arguments.
        return QueueExecution(
            state,
            _compiler,
            _sourceKind,
            _charSource.ToString(),
            _utf8Source.ToArray(),
            _chunkName.ToString(),
            _environmentHandle,
            IntoLuau.CaptureBorrowed(args),
            nResults,
            resultSelector,
            cancellationToken
        );
    }

    // Separate from ExecuteCoreAsync, so that only a queued execution allocates the closure.
    private static unsafe ValueTask<TResult> QueueExecution<TResult>(
        LuauState state,
        LuauCompiler compiler,
        LuauChunkSourceKind sourceKind,
        string charSource,
        byte[] utf8Source,
        string chunkName,
        ulong environmentHandle,
        IntoLuauCopied[] copiedArgs,
        int nResults,
        Func<LuauArgs, TResult> resultSelector,
        CancellationToken cancellationToken
    ) =>
        state.AsyncContext.Queue(() =>
        {
            var chunk = new LuauChunk(
                state,
                compiler,
                sourceKind,
                charSource,
                utf8Source,
                chunkName,
                environmentHandle
            );
            state.ThrowIfDisposed();
            chunk.LoadCompiledChunk(state.L);
            return CoroutineDriver
                .StartInvocation(state, copiedArgs, minResultCount: nResults, cancellationToken)
                .RunAsync(resultSelector, yieldIsError: true);
        });

    private unsafe void LoadCompiledChunk(lua_State* L)
    {
        ReadOnlySpan<char> chunkName = _chunkName.IsEmpty ? DefaultChunkName : _chunkName;
        int nChunkNameBytes = Encoding.UTF8.GetByteCount(chunkName);
        byte[] chunkNameArray = ArrayPool<byte>.Shared.Rent(nChunkNameBytes + 1);
        try
        {
            int actualChunkNameBytes = Encoding.UTF8.GetBytes(chunkName, chunkNameArray);
            chunkNameArray[actualChunkNameBytes] = 0;
            ReadOnlySpan<byte> chunkNameBuffer = chunkNameArray.AsSpan(0, actualChunkNameBytes);

            if (_sourceKind == LuauChunkSourceKind.Utf8Bytes)
            {
                fixed (byte* pChunkName = chunkNameBuffer)
                fixed (byte* pSource = _utf8Source)
                {
                    CompileAndLoadByteCode(L, pSource, (nuint)_utf8Source.Length, pChunkName);
                }
                return;
            }

            int nBytes = Encoding.UTF8.GetByteCount(_charSource);
            byte[] bytesSource = ArrayPool<byte>.Shared.Rent(nBytes);
            try
            {
                Span<byte> bytesSpan = bytesSource.AsSpan(0, nBytes);
                int actualNBytes = Encoding.UTF8.GetBytes(_charSource, bytesSpan);
                bytesSpan = bytesSpan[..actualNBytes];
                fixed (byte* pChunkName = chunkNameBuffer)
                fixed (byte* pSource = bytesSpan)
                {
                    CompileAndLoadByteCode(L, pSource, (nuint)bytesSpan.Length, pChunkName);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytesSource);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunkNameArray);
        }
    }

    private unsafe void CompileAndLoadByteCode(lua_State* L, byte* pSource, nuint sourceLength, byte* pChunkName)
    {
        lua_CompileOptions* pOptions = null;
        if (!ReferenceEquals(_compiler, LuauCompiler.Default))
        {
            var options = new lua_CompileOptions
            {
                optimizationLevel = _compiler.OptimizationLevel,
                debugLevel = _compiler.DebugLevel,
            };
            pOptions = &options;
        }

        nuint nSizeByteCode = 0;
        byte* pByteCode = luau_compile(pSource, sourceLength, pOptions, &nSizeByteCode);
        try
        {
            int environmentStackIndex = 0;
            if (_environmentHandle != 0)
            {
                RegistryReferenceTracker.TrackedReference environmentReference = GetState()
                    .GetTrackedReferenceOrThrow(_environmentHandle);
#pragma warning disable CA2000 // The environment is removed explicitly after luau_load so the loaded function remains on the stack.
                _ = environmentReference.PushToTop();
#pragma warning restore CA2000
                environmentStackIndex = lua_gettop(L);
            }
            try
            {
                int loadStatus = luau_load(L, pChunkName, pByteCode, nSizeByteCode, environmentStackIndex);
                LuaException.ThrowIfNotOk(L, loadStatus, "luau_load");
            }
            finally
            {
                if (environmentStackIndex != 0)
                    lua_remove(L, environmentStackIndex);
            }
        }
        finally
        {
            luau_free(pByteCode);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LuauState GetState()
    {
        ArgumentNullException.ThrowIfNull(_state);
        _state.ThrowIfDisposed();
        return _state;
    }
}
