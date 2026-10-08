using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Darp.Luau.Internal;
using Darp.Luau.Internal.Require;
using Darp.Luau.Native;
using Darp.Luau.Utils;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau;

/// <summary> The LuauState </summary>
/// <remarks> Not threadsafe </remarks>
public sealed unsafe class LuauState : IDisposable
{
    private readonly ILuauFileSystem _virtualFileSystem;

    // ReSharper disable once ReplaceWithFieldKeyword
    internal readonly lua_State* L;
    private int _disposing; // 0 = false, 1 = true
    private readonly ulong _globalsHandle;
    private readonly UserdataRegistrationCache _cache;

    private LuauModuleRequirer? _moduleRequirer;
    private ProtectedTableAccess? _protectedTableAccess;

    // The number of managed callbacks of this state that are running right now.
    private int _callbackDepth;

    internal RegistryReferenceTracker ReferenceTracker { get; }

    /// <summary> Runs async host calls and the continuations of their managed callbacks one turn at a time. </summary>
    internal LuauSynchronizationContext AsyncContext { get; }

    /// <summary> The async host calls in progress, for the managed callbacks they run. </summary>
    internal AsyncDriveTable AsyncDrives { get; } = new();

    private readonly List<GCHandle> _callbackHandles = [];

    /// <summary> The global table. Used as a entry point </summary>
    public LuauTable Globals => new(this, _globalsHandle);

    /// <summary> If true, the LuauState is disposed and any method will throw </summary>
    public bool IsDisposed => _disposing > 0;

    /// <summary>The effective set of built-in libraries loaded into this state.</summary>
    public LuauLibraries EnabledLibraries { get; private set; }

    /// <summary>
    /// Gets current memory-related tracking counters for this state.
    /// </summary>
    internal LuauMemoryStatistics MemoryStatistics =>
        ReferenceTracker.GetStatistics(
            activeManagedCallbacks: _callbackHandles.Count(static handle => handle.IsAllocated)
        );

    /// <summary> Initializes a new LuauState, and opens all default libs. </summary>
    /// <exception cref="InvalidOperationException"> Thrown if the luau state could not be created </exception>
    public LuauState()
        : this(LuauLibraries.All) { }

    /// <summary>Initializes a new LuauState with explicit standard library loading options.</summary>
    /// <param name="builtinLibraries">Standard Luau libraries to load.</param>
    /// <param name="virtualFileSystem">A virtual filesystem for file operations</param>
    /// <exception cref="InvalidOperationException">Thrown if the Luau state could not be created.</exception>
    public LuauState(LuauLibraries builtinLibraries, ILuauFileSystem? virtualFileSystem = null)
        : this(builtinLibraries, virtualFileSystem, hostSynchronizationContext: null) { }

    /// <summary>Initializes a new LuauState whose async work runs on a host dispatcher.</summary>
    /// <param name="builtinLibraries">Standard Luau libraries to load.</param>
    /// <param name="virtualFileSystem">A virtual filesystem for file operations</param>
    /// <param name="hostSynchronizationContext">
    /// A single-threaded dispatcher of the host, such as a UI thread's context. When given, async host calls and
    /// the continuations of async managed callbacks run on it: calls started on another thread are posted to it.
    /// When <c>null</c>, they run on the calling thread and on thread-pool threads, one at a time.
    /// <see cref="SynchronizationContext.Current"/> is never captured implicitly.
    /// </param>
    /// <exception cref="InvalidOperationException">Thrown if the Luau state could not be created.</exception>
    public LuauState(
        LuauLibraries builtinLibraries,
        ILuauFileSystem? virtualFileSystem,
        SynchronizationContext? hostSynchronizationContext
    )
    {
        _virtualFileSystem = virtualFileSystem ?? new FileSystem();
        AsyncContext = new LuauSynchronizationContext(hostSynchronizationContext);

        L = luaL_newstate();
        if (L is null)
            throw new InvalidOperationException("Could not create Lua state.");
        try
        {
#if DEBUG
            using var guard = new StackGuard(L, expectedDelta: 0);
#endif

            _cache = new UserdataRegistrationCache(this);
            ReferenceTracker = new RegistryReferenceTracker(this);

            LoadStandardLibraries(builtinLibraries);

            // Push table to stack, get the reference and pop
            lua_pushvalue(L, LUA_GLOBALSINDEX);
            _globalsHandle = ReferenceTracker.TrackAndPopRef(L, -1, pinned: true);
        }
        catch
        {
            // Nobody gets the state to dispose it.
            _disposing = 1;
            lua_close(L);
            throw;
        }
    }

    /// <summary>Loads the requested standard Luau libraries into this state.</summary>
    /// <param name="libraries">Libraries to load. Already loaded libraries are ignored.</param>
    public void LoadStandardLibraries(LuauLibraries libraries)
    {
        this.ThrowIfDisposed();
        LuauLibraries missingLibraries = libraries & ~EnabledLibraries;
        if (missingLibraries == 0)
            return;

        OpenBuiltinLibraries(missingLibraries);
        EnabledLibraries |= missingLibraries;
    }

    private void OpenBuiltinLibraries(LuauLibraries libraries)
    {
        if (libraries.HasFlag(LuauLibraries.Base))
            openlib(L, ""u8, luaopen_base);
        if (libraries.HasFlag(LuauLibraries.Coroutine))
            openlib(L, LUA_COLIBNAME, luaopen_coroutine);
        if (libraries.HasFlag(LuauLibraries.Table))
            openlib(L, LUA_TABLIBNAME, luaopen_table);
        if (libraries.HasFlag(LuauLibraries.Os))
            openlib(L, LUA_OSLIBNAME, luaopen_os);
        if (libraries.HasFlag(LuauLibraries.String))
            openlib(L, LUA_STRLIBNAME, luaopen_string);
        if (libraries.HasFlag(LuauLibraries.Math))
            openlib(L, LUA_MATHLIBNAME, luaopen_math);
        if (libraries.HasFlag(LuauLibraries.Debug))
            openlib(L, LUA_DBLIBNAME, luaopen_debug);
        if (libraries.HasFlag(LuauLibraries.Utf8))
            openlib(L, LUA_UTF8LIBNAME, luaopen_utf8);
        if (libraries.HasFlag(LuauLibraries.Bit32))
            openlib(L, LUA_BITLIBNAME, luaopen_bit32);
        if (libraries.HasFlag(LuauLibraries.Buffer))
            openlib(L, LUA_BUFFERLIBNAME, luaopen_buffer);
        if (libraries.HasFlag(LuauLibraries.Vector))
            openlib(L, LUA_VECLIBNAME, luaopen_vector);
    }

    private delegate int OpenLibFunc(lua_State* L);

    private static void openlib(lua_State* L, ReadOnlySpan<byte> name, OpenLibFunc openf)
    {
#if DEBUG
        using var guard = new StackGuard(L, expectedDelta: 0);
#endif
        IntPtr intPtr = Marshal.GetFunctionPointerForDelegate(openf);
        lua_pushcfunction(L, (delegate* unmanaged[Cdecl]<lua_State*, int>)intPtr, null);
        fixed (byte* pName = name)
            lua_pushstring(L, pName);
        lua_call(L, 1, 0);
    }

    /// <summary> The delegate type used to load a custom module table. </summary>
    /// <param name="state"> The <see cref="LuauState"/> the module is registered for. </param>
    /// <param name="module"> The module table to populate. </param>
    public delegate void OnModuleLoad(LuauState state, in LuauTable module);

    /// <summary>Registers a host-provided module that can be loaded with <c>require("name")</c>.</summary>
    /// <param name="name">Require name of the module.</param>
    /// <param name="onLoad">Callback used to populate the module table when it is first required.</param>
    public void RegisterModule(string name, OnModuleLoad onLoad)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(onLoad);
        this.ThrowIfDisposed();

        if (IsReservedModuleName(name))
            throw new ArgumentException(
                $"Module name '{name}' conflicts with script module require prefixes.",
                nameof(name)
            );

        LuauModuleRequirer requirer = GetOrCreateModuleRequirer();
        requirer.RegisterHostModule(name, onLoad);
    }

    /// <summary>Registers a host-provided module using its strongly typed module contract.</summary>
    /// <param name="module">Module instance to register.</param>
    /// <typeparam name="TModule">Module type.</typeparam>
    public void RegisterModule<TModule>(TModule module)
        where TModule : ILuauModule<TModule>
    {
        ArgumentNullException.ThrowIfNull(module);
        RegisterModule(TModule.ModuleName, module.OnLoad);
    }

    /// <summary>Registers a host-provided module using its strongly typed module contract.</summary>
    /// <returns>The created module</returns>
    /// <typeparam name="TModule">Module type.</typeparam>
    public TModule RegisterModule<TModule>()
        where TModule : ILuauModule<TModule>, new()
    {
        var module = new TModule();
        RegisterModule(TModule.ModuleName, module.OnLoad);
        return module;
    }

    /// <summary>Enables file-backed script modules for <see cref="LuauState"/>.</summary>
    public void EnableScriptModules()
    {
        this.ThrowIfDisposed();
        GetOrCreateModuleRequirer().EnableScriptModules();
    }

    internal LuauModuleRequirer GetOrCreateModuleRequirer()
    {
        _moduleRequirer ??= new LuauModuleRequirer(this, _virtualFileSystem);
        return _moduleRequirer;
    }

    /// <summary> Reads and writes tables whose metamethods may run script code. </summary>
    internal ProtectedTableAccess ProtectedTableAccess => _protectedTableAccess ??= new ProtectedTableAccess(this);

    /// <summary> Marks a managed callback of this state as running until the scope is disposed. </summary>
    internal CallbackScope EnterCallback()
    {
        _callbackDepth++;
        return new CallbackScope(this);
    }

    /// <summary> A running managed callback. The state cannot be disposed while one exists. </summary>
    internal readonly ref struct CallbackScope(LuauState state)
    {
        [SuppressMessage(
            "Usage",
            "CA2213:Disposable fields should be disposed",
            Justification = "The scope references the state but does not own it."
        )]
        private readonly LuauState _state = state;

        public void Dispose() => _state._callbackDepth--;
    }

    internal bool OwnsThread(lua_State* luaState)
    {
        ArgumentNullException.ThrowIfNull(luaState);
        // Most calls run on the main thread; only coroutines pay for the lookup.
        return luaState == L || lua_mainthread(luaState) == L;
    }

    private static bool IsReservedModuleName(string name) =>
        name.StartsWith('.')
        || name.StartsWith('/')
        || name.StartsWith('\\')
        || name.StartsWith('@')
        || name.Contains('/', StringComparison.Ordinal)
        || name.Contains('\\', StringComparison.Ordinal);

    internal void PushNativeCallback(
        delegate* unmanaged[Cdecl]<lua_State*, void*, int> callback,
        void* context,
        byte* debugName = null
    )
    {
        this.ThrowIfDisposed();
#if DEBUG
        using var guard = new StackGuard(L, expectedDelta: 1);
#endif
        darp_luau_pushcallback(L, callback, context, debugName);
    }

    internal GCHandle TrackCallbackContext(object context)
    {
        this.ThrowIfDisposed();
        var handle = GCHandle.Alloc(context);
        _callbackHandles.Add(handle);
        return handle;
    }

    /// <summary> Creates a coroutine that runs <paramref name="body"/> when it is first resumed. </summary>
    /// <param name="body">The function the coroutine runs.</param>
    /// <returns>The created coroutine, in status <see cref="LuauCoroutineStatus.Suspended"/>.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the state or <paramref name="body"/> is disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="body"/> belongs to a different state.</exception>
    public LuauCoroutine CreateCoroutine(LuauFunction body)
    {
        this.ThrowIfDisposed();
        _ = body.GetHandleOrThrow();
#if DEBUG
        using var guard = new StackGuard(L, expectedDelta: 0);
#endif
        lua_State* coroutine = lua_newthread(L);
        try
        {
            IntoLuau bodyValue = body;
            bodyValue.Push(this, coroutine);
        }
        catch
        {
            lua_pop(L, 1);
            throw;
        }
        ulong reference = ReferenceTracker.TrackAndPopRef(L, -1);
        return new LuauCoroutine(this, reference);
    }

    /// <summary> Create a new table </summary>
    /// <returns> The resulting table </returns>
    public LuauTable CreateTable()
    {
        this.ThrowIfDisposed();
#if DEBUG
        using var guard = new StackGuard(L, expectedDelta: 0);
#endif
        lua_newtable(L);
        ulong reference = ReferenceTracker.TrackAndPopRef(L, -1);
        return new LuauTable(this, reference);
    }

    /// <summary>
    /// Creates a writable chunk environment table that falls back to this state's globals.
    /// </summary>
    /// <returns>The created environment table.</returns>
    public LuauTable CreateEnvironment()
    {
        this.ThrowIfDisposed();
#if DEBUG
        using var guard = new StackGuard(L, expectedDelta: 0);
#endif
        lua_newtable(L);

        fixed (byte* pGlobalName = "_G\0"u8)
        {
            lua_pushvalue(L, -1);
            lua_setfield(L, -2, pGlobalName);
        }

        lua_newtable(L);
        fixed (byte* pIndexName = "__index\0"u8)
        {
            lua_pushvalue(L, LUA_GLOBALSINDEX);
            lua_setfield(L, -2, pIndexName);
        }

        _ = lua_setmetatable(L, -2);

        ulong reference = ReferenceTracker.TrackAndPopRef(L, -1);
        return new LuauTable(this, reference);
    }

    /// <summary>
    /// Manual callback delegate used by <see cref="CreateFunctionBuilder(LuauFunctionBuilder)"/>.
    /// </summary>
    /// <remarks>
    /// Use this low-level shape when you need direct access to <see cref="LuauArgs"/>,
    /// custom validation or error handling, or a callback shape that the generator-backed path does not support.
    /// </remarks>
    public delegate LuauReturn LuauFunctionBuilder(LuauArgs args);

    /// <summary>
    /// Creates a Luau function from a manual callback builder.
    /// </summary>
    /// <param name="onCalled">Callback that reads its inputs from <see cref="LuauArgs"/> and returns a <see cref="LuauReturn"/>.</param>
    /// <returns>The created <see cref="LuauFunction"/>.</returns>
    /// <remarks>
    /// Prefer <see cref="CreateFunction{T}(T)"/> for normal typed delegates.
    /// Use this method when you need manual argument parsing, custom error handling, or a callback shape that the generator-backed path does not support.
    /// </remarks>
    public LuauFunction CreateFunctionBuilder(LuauFunctionBuilder onCalled)
    {
        this.ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(onCalled);
#if DEBUG
        using var guard = new StackGuard(L, expectedDelta: 0);
#endif
        var context = new FunctionBuilderCallbackContext(this, onCalled);
        GCHandle handle = TrackCallbackContext(context);
        fixed (byte* pDebugName = "managed function\0"u8)
        {
            PushNativeCallback(&FunctionBuilderCallback, (void*)GCHandle.ToIntPtr(handle), pDebugName);
        }
        ulong reference = ReferenceTracker.TrackAndPopRef(L, -1);
        return new LuauFunction(this, reference);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int FunctionBuilderCallback(lua_State* luaState, void* ctx)
    {
        ArgumentNullException.ThrowIfNull(luaState);
        int topBeforeCallback = lua_gettop(luaState);
        try
        {
            var handle = GCHandle.FromIntPtr((IntPtr)ctx);
            if (handle.Target is not FunctionBuilderCallbackContext context)
                return LuauStateMarshal.ReturnError(luaState, "managed function callback context is invalid");

            return context.Invoke(luaState);
        }
        catch (Exception exception)
        {
            lua_settop(luaState, topBeforeCallback);
            return LuauStateMarshal.ReturnCallbackException(luaState, "managed function", exception);
        }
    }

    private sealed class FunctionBuilderCallbackContext(LuauState state, LuauFunctionBuilder onCalled)
    {
        private readonly LuauState _state = state;
        private readonly LuauFunctionBuilder _onCalled = onCalled;

        public int Invoke(lua_State* luaState)
        {
            LuauState state = _state;
            if (!state.OwnsThread(luaState))
                return LuauStateMarshal.ReturnError(luaState, "managed function callback belongs to a different state");

            using CallbackScope callbackScope = state.EnterCallback();
            int numberOfParameters = lua_gettop(luaState);
#if DEBUG
            using var nestedGuard = new StackGuard(luaState, expectedDelta: 0);
#endif
            int topBeforeInvoke = numberOfParameters;
            var args = new LuauArgs(state, luaState, numberOfParameters, firstParameterStackIndex: 1);
            try
            {
                LuauReturn result = _onCalled(args);
                Debug.Assert(lua_gettop(luaState) == topBeforeInvoke);

                int returnCount = LuauStateMarshal.ReturnCallbackResult(state, luaState, result);
#if DEBUG
                // A yield leaves the stack as it is; an error leaves its message.
                nestedGuard.OverwriteExpectedDelta(
                    returnCount switch
                    {
                        DARP_LUAU_CALLBACK_YIELD => 0,
                        < 0 => 1,
                        _ => returnCount,
                    }
                );
#endif
                return returnCount;
            }
            catch (Exception exception)
            {
                lua_settop(luaState, topBeforeInvoke);
                int returnCount = LuauStateMarshal.ReturnCallbackException(luaState, "managed function", exception);
#if DEBUG
                nestedGuard.OverwriteExpectedDelta(1);
#endif
                return returnCount;
            }
        }
    }

    /// <summary>
    /// Creates a Luau function from a supported managed delegate signature.
    /// </summary>
    /// <param name="value">Delegate to expose to Luau.</param>
    /// <typeparam name="T">Concrete delegate type used for compile-time signature analysis.</typeparam>
    /// <returns>The created <see cref="LuauFunction"/>.</returns>
    /// <remarks>
    /// This method must be invoked directly so the generator can intercept the call site and emit a marshalling adapter.
    /// The runtime stub always throws if interception does not happen.
    /// Use <see cref="CreateFunctionBuilder(LuauFunctionBuilder)"/> when you need manual argument handling,
    /// custom error handling, or a delegate shape that is not supported by the generator.
    /// </remarks>
#pragma warning disable CA1822 // Mark members as static
    public LuauFunction CreateFunction<T>(T value)
#pragma warning restore CA1822 // The instance method shape is required for source-generator interception and consistent LuauState API usage.
        where T : Delegate => throw new InvalidOperationException("This method should be intercepted!");

    /// <summary>
    /// Gets an existing userdata for this managed instance or creates a new one.
    /// </summary>
    /// <param name="userdata">Managed userdata instance.</param>
    /// <typeparam name="T">Managed userdata type.</typeparam>
    /// <returns>A Lua userdata reference associated with <paramref name="userdata"/>.</returns>
    public LuauUserdata GetOrCreateUserdata<T>(T userdata)
        where T : class, ILuauUserData<T>
    {
        this.ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(userdata);
        return _cache.GetOrCreate(userdata);
    }

    /// <summary>Creates a new Luau string from UTF-16 text.</summary>
    /// <param name="value">The string content to encode as UTF-8.</param>
    /// <returns>The created <see cref="LuauString"/> reference.</returns>
    public LuauString CreateString(scoped ReadOnlySpan<char> value)
    {
        using var utf8 = new Utf8Buffer(value, stackalloc byte[Utf8Buffer.StackSize]);
        return CreateString(utf8.Bytes);
    }

    /// <summary>Creates a new Luau string from UTF-8 bytes.</summary>
    /// <param name="utf8Value">The UTF-8 encoded string content.</param>
    /// <returns>The created <see cref="LuauString"/> reference.</returns>
    public LuauString CreateString(scoped ReadOnlySpan<byte> utf8Value)
    {
        this.ThrowIfDisposed();
        if (utf8Value.IsEmpty)
            throw new ArgumentNullException(nameof(utf8Value));
#if DEBUG
        using var guard = new StackGuard(L, expectedDelta: 0);
#endif
        fixed (byte* pValue = utf8Value)
        {
            lua_pushlstring(L, pValue, (nuint)utf8Value.Length);
        }

        ulong reference = ReferenceTracker.TrackAndPopRef(L, -1);
        return new LuauString(this, reference);
    }

    /// <summary>Creates a new Luau buffer from managed bytes.</summary>
    /// <param name="span">The bytes to copy into the new buffer.</param>
    /// <returns>The created <see cref="LuauBuffer"/> reference.</returns>
    public LuauBuffer CreateBuffer(scoped ReadOnlySpan<byte> span)
    {
        this.ThrowIfDisposed();
        ObjectDisposedException.ThrowIf(_disposing > 0, this);
        if (span.IsEmpty)
            throw new ArgumentNullException(nameof(span));
#if DEBUG
        using var guard = new StackGuard(L, expectedDelta: 0);
#endif
        void* pDest = lua_newbuffer(L, (nuint)span.Length);

        fixed (byte* pSrc = span)
        {
            Unsafe.CopyBlock(pDest, pSrc, (uint)span.Length);
        }

        ulong handle = ReferenceTracker.TrackAndPopRef(L, -1);
        return new LuauBuffer(this, handle);
    }

    /// <summary> Creates a lightweight executable chunk from source text. </summary>
    /// <param name="source">The source text to compile and execute.</param>
    /// <returns>A chunk wrapper that can be executed or converted to a reusable <see cref="LuauFunction"/>.</returns>
    /// <remarks>
    /// The returned chunk is an ephemeral wrapper over the provided source. Executing it recompiles the source each time.
    /// </remarks>
    public LuauChunk Load(ReadOnlySpan<char> source)
    {
        this.ThrowIfDisposed();
        return new LuauChunk(this, LuauCompiler.Default, source).WithName("=stdin");
    }

    /// <summary> Creates a lightweight executable chunk from UTF-8 source bytes. </summary>
    /// <param name="source">The UTF-8 encoded source text to compile and execute.</param>
    /// <returns>A chunk wrapper that can be executed or converted to a reusable <see cref="LuauFunction"/>.</returns>
    /// <remarks>
    /// The returned chunk is an ephemeral wrapper over the provided source. Executing it recompiles the source each time.
    /// </remarks>
    public LuauChunk Load(ReadOnlySpan<byte> source)
    {
        this.ThrowIfDisposed();
        return new LuauChunk(this, LuauCompiler.Default, source).WithName("=stdin");
    }

    /// <summary>
    /// Creates a lightweight executable chunk from the content of a source file.
    /// </summary>
    /// <param name="path">The path to the <c>.luau</c> file</param>
    /// <returns>A chunk wrapper that can be executed or converted to a reusable <see cref="LuauFunction"/>.</returns>
    /// <remarks>
    /// The returned chunk is an ephemeral wrapper over the provided source. Executing it recompiles the source each time.
    /// </remarks>
    public LuauChunk LoadFile(string path)
    {
        this.ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(path);
        string? source = _virtualFileSystem.ReadFile(path);
        if (source is null)
            throw new FileNotFoundException(path);
        string chunkName = '@' + FileUtils.NormalizePath(path);
        return new LuauChunk(this, LuauCompiler.Default, source).WithName(chunkName);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// Thrown when a managed callback of this state is running. Luau would continue in a closed state when the
    /// callback returns. Dispose the state after the host call that runs the script has returned.
    /// </exception>
    [SuppressMessage(
        "Design",
        "CA1065:Do not raise exceptions in unexpected locations",
        Justification = "Closing the state under a running callback would let Luau continue in freed memory."
    )]
    public void Dispose()
    {
        if (_callbackDepth > 0 && _disposing == 0)
        {
            throw new InvalidOperationException(
                "A LuauState cannot be disposed while one of its managed callbacks runs. "
                    + "Dispose it after the host call that runs the script has returned."
            );
        }
        if (Interlocked.Exchange(ref _disposing, 1) != 0)
            return;
        _cache.Dispose();
        _moduleRequirer?.Dispose();
        _moduleRequirer = null;
        ReferenceTracker.ReleaseAll();
        lua_close(L);
        foreach (GCHandle callbackHandle in _callbackHandles)
        {
            if (callbackHandle.IsAllocated)
                callbackHandle.Free();
        }
        _callbackHandles.Clear();
    }
}
