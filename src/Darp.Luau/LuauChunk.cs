using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using Darp.Luau.Internal.Async;
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

    /// <summary> Compiles and executes the chunk asynchronously, ignoring any return values. </summary>
    public ValueTask ExecuteAsync(params RefEnumerable<IntoLuau> args) => ExecuteAsync(args, CancellationToken.None);

    /// <summary> Compiles and executes the chunk asynchronously, ignoring any return values. </summary>
    /// <param name="args">The arguments passed to the chunk.</param>
    /// <param name="token">Cancellation token passed to async managed callbacks.</param>
    public ValueTask ExecuteAsync(RefEnumerable<IntoLuau> args, CancellationToken token)
    {
        LuauState state = GetState();
        IntoLuauCopied[] copiedArgs = CaptureArgs(args);
        LuauAsyncChunkSource source;
        try
        {
            source = CreateAsyncSource(state);
        }
        catch
        {
            ReleaseArgs(copiedArgs);
            throw;
        }

        var frame = new LuauAsyncVoidCoroutineFrame(state, source, copiedArgs, token);
        state.EnqueueAsyncWork(frame.Start);
        return frame.VoidValueTask;
    }

    /// <summary> Compiles and executes the chunk and converts the first return value. </summary>
    /// <param name="args">The arguments passed to the chunk.</param>
    /// <typeparam name="TR">Managed return type to convert to.</typeparam>
    /// <returns>The first Luau return value converted to <typeparamref name="TR"/>.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the owning state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a load or runtime error.</exception>
    /// <exception cref="InvalidCastException">Thrown when the return value cannot be converted to <typeparamref name="TR"/>.</exception>
    public TR Execute<TR>(params RefEnumerable<IntoLuau> args) =>
        ExecuteCore(args, nResults: 1, LuauFunctionInvokeCore.ResultSelector<TR>);

    /// <summary> Compiles and executes the chunk asynchronously and converts the first return value. </summary>
    public ValueTask<TR> ExecuteAsync<TR>(params RefEnumerable<IntoLuau> args) =>
        ExecuteAsync(args, LuauFunctionInvokeCore.ResultSelector<TR>, CancellationToken.None);

    /// <summary> Compiles and executes the chunk asynchronously and converts the first return value. </summary>
    /// <param name="args">The arguments passed to the chunk.</param>
    /// <param name="token">Cancellation token passed to async managed callbacks.</param>
    /// <typeparam name="TR">Managed return type to convert to.</typeparam>
    public ValueTask<TR> ExecuteAsync<TR>(RefEnumerable<IntoLuau> args, CancellationToken token) =>
        ExecuteAsync(args, LuauFunctionInvokeCore.ResultSelector<TR>, token);

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

    /// <summary> Compiles and executes the chunk asynchronously and converts the first two return values. </summary>
    public ValueTask<(TR1, TR2)> ExecuteAsync<TR1, TR2>(params RefEnumerable<IntoLuau> args) =>
        ExecuteAsync(args, LuauFunctionInvokeCore.ResultSelector<TR1, TR2>, CancellationToken.None);

    /// <summary> Compiles and executes the chunk asynchronously and converts the first two return values. </summary>
    public ValueTask<(TR1, TR2)> ExecuteAsync<TR1, TR2>(RefEnumerable<IntoLuau> args, CancellationToken token) =>
        ExecuteAsync(args, LuauFunctionInvokeCore.ResultSelector<TR1, TR2>, token);

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

    /// <summary> Compiles and executes the chunk asynchronously and converts the first three return values. </summary>
    public ValueTask<(TR1, TR2, TR3)> ExecuteAsync<TR1, TR2, TR3>(params RefEnumerable<IntoLuau> args) =>
        ExecuteAsync(args, LuauFunctionInvokeCore.ResultSelector<TR1, TR2, TR3>, CancellationToken.None);

    /// <summary> Compiles and executes the chunk asynchronously and converts the first three return values. </summary>
    public ValueTask<(TR1, TR2, TR3)> ExecuteAsync<TR1, TR2, TR3>(
        RefEnumerable<IntoLuau> args,
        CancellationToken token
    ) => ExecuteAsync(args, LuauFunctionInvokeCore.ResultSelector<TR1, TR2, TR3>, token);

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

    /// <summary> Compiles and executes the chunk asynchronously and converts the first four return values. </summary>
    public ValueTask<(TR1, TR2, TR3, TR4)> ExecuteAsync<TR1, TR2, TR3, TR4>(
        params RefEnumerable<IntoLuau> args
    ) => ExecuteAsync(args, LuauFunctionInvokeCore.ResultSelector<TR1, TR2, TR3, TR4>, CancellationToken.None);

    /// <summary> Compiles and executes the chunk asynchronously and converts the first four return values. </summary>
    public ValueTask<(TR1, TR2, TR3, TR4)> ExecuteAsync<TR1, TR2, TR3, TR4>(
        RefEnumerable<IntoLuau> args,
        CancellationToken token
    ) => ExecuteAsync(args, LuauFunctionInvokeCore.ResultSelector<TR1, TR2, TR3, TR4>, token);

    /// <summary>
    /// Compiles and executes the chunk and returns all Luau return values as raw <see cref="LuauValue"/> instances.
    /// </summary>
    /// <param name="args">The arguments passed to the chunk.</param>
    /// <returns>All Luau return values as an array.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the owning state is disposed.</exception>
    /// <exception cref="LuaException">Thrown when Luau reports a load or runtime error.</exception>
    public LuauValue[] ExecuteMulti(params RefEnumerable<IntoLuau> args) =>
        ExecuteCore(args, nResults: LuaMultRet, LuauFunctionInvokeCore.ResultSelectorMulti);

    /// <summary> Compiles and executes the chunk asynchronously and returns all Luau return values. </summary>
    public ValueTask<LuauValue[]> ExecuteMultiAsync(params RefEnumerable<IntoLuau> args) =>
        ExecuteAsync(args, LuauFunctionInvokeCore.ResultSelectorMulti, CancellationToken.None);

    /// <summary> Compiles and executes the chunk asynchronously and returns all Luau return values. </summary>
    public ValueTask<LuauValue[]> ExecuteMultiAsync(RefEnumerable<IntoLuau> args, CancellationToken token) =>
        ExecuteAsync(args, LuauFunctionInvokeCore.ResultSelectorMulti, token);

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
        RefEnumerable<IntoLuau> args,
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
            using LuauCallFrame frame = state.BeginLuauCallFrame();
            var result = new LuauArgs(state.L, lua_gettop(L) - topBeforeInvoke, topBeforeInvoke + 1, frame);
            return resultSelector(result);
        }
        finally
        {
            lua_settop(L, topBeforeInvoke);
        }
    }

    private ValueTask<TResult> ExecuteAsync<TResult>(
        RefEnumerable<IntoLuau> args,
        Func<LuauArgs, TResult> resultSelector,
        CancellationToken token
    )
    {
        LuauState state = GetState();
        IntoLuauCopied[] copiedArgs = CaptureArgs(args);
        LuauAsyncChunkSource source;
        try
        {
            source = CreateAsyncSource(state);
        }
        catch
        {
            ReleaseArgs(copiedArgs);
            throw;
        }

        var frame = new LuauAsyncCoroutineFrame<TResult>(state, source, copiedArgs, resultSelector, token);
        state.EnqueueAsyncWork(frame.Start);
        return frame.ValueTask;
    }

    private LuauAsyncChunkSource CreateAsyncSource(LuauState state)
    {
        ReadOnlySpan<char> chunkName = _chunkName.IsEmpty ? DefaultChunkName : _chunkName;
        return new LuauAsyncChunkSource(
            state,
            _compiler,
            _sourceKind,
            _charSource,
            _utf8Source,
            chunkName,
            _environmentHandle
        );
    }

    private static IntoLuauCopied[] CaptureArgs(RefEnumerable<IntoLuau> args)
    {
        var copiedArgs = new IntoLuauCopied[args.Length];
        int captured = 0;
        try
        {
            for (; captured < args.Length; captured++)
                copiedArgs[captured] = args[captured].CaptureCopied();
            return copiedArgs;
        }
        catch
        {
            for (int i = 0; i < captured; i++)
                copiedArgs[i].Release();
            throw;
        }
    }

    private static void ReleaseArgs(IntoLuauCopied[] args)
    {
        foreach (IntoLuauCopied arg in args)
            arg.Release();
    }

    private unsafe void ExecuteCore(RefEnumerable<IntoLuau> args, int nResults)
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

    private unsafe void LoadCompiledChunk(lua_State* L)
    {
        ReadOnlySpan<char> chunkName = _chunkName.IsEmpty ? DefaultChunkName : _chunkName;
        int nChunkNameBytes = Encoding.UTF8.GetByteCount(chunkName);
        byte[] chunkNameArray = ArrayPool<byte>.Shared.Rent(nChunkNameBytes + 1);
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
