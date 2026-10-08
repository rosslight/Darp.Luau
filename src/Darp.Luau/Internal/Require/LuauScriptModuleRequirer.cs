using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Darp.Luau.Native;
using Darp.Luau.Utils;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Internal.Require;

/// <summary> A require-by-string requirer that uses a virtual file system to resolve module paths. </summary>
/// <seealso href="https://github.com/luau-lang/luau/blob/master/CLI/src/ReplRequirer.cpp"/>
internal sealed unsafe class LuauScriptModuleRequirer : IDisposable
{
    private const byte ChunkNamePrefix = (byte)'@';
    private readonly ILuauFileSystem _virtualFileSystem;
    private readonly LuauModuleNavigator _navigator;
    private GCHandle _handle;
    private darp_luau_require_context_data* _requireContext;

    /// <summary> A require-by-string requirer that uses a virtual file system to resolve module paths. </summary>
    /// <param name="virtualFileSystem">A virtual file system for abstract file operations</param>
    /// <seealso href="https://github.com/luau-lang/luau/blob/master/CLI/src/ReplRequirer.cpp"/>
    public LuauScriptModuleRequirer(ILuauFileSystem virtualFileSystem)
    {
        _virtualFileSystem = virtualFileSystem;
        _navigator = new LuauModuleNavigator(virtualFileSystem);
        _handle = GCHandle.Alloc(this);
        _requireContext = darp_luau_newrequirecontext(&InitRequireConfig, &Load, ToVoidPtr());
        if (_requireContext is null)
        {
            _handle.Free();
            throw new InvalidOperationException("Could not create Luau require context.");
        }
    }

    public bool Enabled { get; set; }

    public RequireResolution Resolve(string path)
    {
        if (IsScriptModulePath(path))
        {
            return Enabled
                ? RequireResolution.ScriptModule
                : RequireResolution.LoadError("script module require is not enabled");
        }

        return IsInvalidScriptModulePath(path)
            ? RequireResolution.LoadError("require path must start with a valid prefix: ./, ../, or @")
            : RequireResolution.NotFound;
    }

    private static bool IsScriptModulePath(string path) =>
        path.StartsWith("./", StringComparison.Ordinal)
        || path.StartsWith("../", StringComparison.Ordinal)
        || path.StartsWith('@');

    private static bool IsInvalidScriptModulePath(string path) =>
        path.Contains('/', StringComparison.Ordinal) || path.Contains('\\', StringComparison.Ordinal);

    internal LuauFunction CreateProxyRequireFunction(LuauState state)
    {
#if DEBUG
        using var guard = new StackGuard(state.L, expectedDelta: 0);
#endif
        _ = darp_luau_pushproxyrequire(state.L, _requireContext);
        ulong reference = state.ReferenceTracker.TrackAndPopRef(state.L, -1);
        return new LuauFunction(state, reference);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void InitRequireConfig(luarequire_Configuration* config)
    {
        config->is_require_allowed = &IsRequireAllowed;
        config->reset = &Reset;
        config->jump_to_alias = &JumpToAlias;
        config->to_parent = &ToParent;
        config->to_child = &ToChild;
        config->is_module_present = &IsModulePresent;
        config->get_chunkname = &GetChunkname;
        config->get_loadname = &GetLoadname;
        config->get_cache_key = &GetCacheKey;
        config->get_config_status = &GetConfigStatus;
        config->get_config = &GetConfig;
        // load is set using darp_luau_newrequirecontext and uses a different API which allows us to return errors
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static bool IsRequireAllowed(lua_State* L, void* ctx, byte* requirerChunkname)
    {
        // Require should be allowed only for
        // - Chunks with relative (preferred), or absolute paths -> have to start with an '@'
        // - Chunks that are named '=stdin'
        ReadOnlySpan<byte> chunkName = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(requirerChunkname);
        return chunkName.SequenceEqual("=stdin"u8) || chunkName.StartsWith(ChunkNamePrefix);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static luarequire_NavigateResult Reset(lua_State* L, void* ctx, byte* requirerChunkname)
    {
        try
        {
            LuauScriptModuleRequirer req = FromVoidPtr(ctx);

            ReadOnlySpan<byte> chunkName = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(requirerChunkname);
            if (chunkName.SequenceEqual("=stdin"u8))
                return req._navigator.ResetToStdIn();
            if (chunkName.StartsWith(ChunkNamePrefix))
                return req._navigator.ResetToPath(Encoding.UTF8.GetString(chunkName[1..]));

            return luarequire_NavigateResult.NAVIGATE_NOT_FOUND;
        }
        catch (Exception)
        {
            return luarequire_NavigateResult.NAVIGATE_NOT_FOUND;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static luarequire_NavigateResult JumpToAlias(lua_State* L, void* ctx, byte* path)
    {
        try
        {
            LuauScriptModuleRequirer req = FromVoidPtr(ctx);

            string strPath = ReadUtf8Z(path);
            if (!FileUtils.IsAbsolutePath(strPath))
                return luarequire_NavigateResult.NAVIGATE_NOT_FOUND;

            return req._navigator.ResetToPath(strPath);
        }
        catch (Exception)
        {
            return luarequire_NavigateResult.NAVIGATE_NOT_FOUND;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static luarequire_NavigateResult ToParent(lua_State* L, void* ctx)
    {
        try
        {
            return FromVoidPtr(ctx)._navigator.ToParent();
        }
        catch (Exception)
        {
            return luarequire_NavigateResult.NAVIGATE_NOT_FOUND;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static luarequire_NavigateResult ToChild(lua_State* L, void* ctx, byte* name)
    {
        try
        {
            return FromVoidPtr(ctx)._navigator.ToChild(ReadUtf8Z(name));
        }
        catch (Exception)
        {
            return luarequire_NavigateResult.NAVIGATE_NOT_FOUND;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static bool IsModulePresent(lua_State* L, void* ctx)
    {
        try
        {
            LuauScriptModuleRequirer req = FromVoidPtr(ctx);

            return req._virtualFileSystem.FileExists(req._navigator.RealPath);
        }
        catch (Exception)
        {
            return false;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static luarequire_WriteResult GetChunkname(
        lua_State* L,
        void* ctx,
        byte* buffer,
        nuint bufferSize,
        nuint* sizeOut
    )
    {
        return Write(ctx, static req => $"@{req._navigator.RealPath}", buffer, bufferSize, sizeOut);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static luarequire_WriteResult GetLoadname(
        lua_State* L,
        void* ctx,
        byte* buffer,
        nuint bufferSize,
        nuint* sizeOut
    )
    {
        return Write(ctx, static req => req._navigator.AbsoluteRealPath, buffer, bufferSize, sizeOut);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static luarequire_WriteResult GetCacheKey(
        lua_State* L,
        void* ctx,
        byte* buffer,
        nuint bufferSize,
        nuint* sizeOut
    )
    {
        return Write(ctx, static req => req._navigator.AbsoluteRealPath, buffer, bufferSize, sizeOut);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static luarequire_ConfigStatus GetConfigStatus(lua_State* L, void* ctx)
    {
        try
        {
            return FromVoidPtr(ctx)._navigator.GetConfigStatus();
        }
        catch (Exception)
        {
            return luarequire_ConfigStatus.CONFIG_ABSENT;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static luarequire_WriteResult GetConfig(
        lua_State* L,
        void* ctx,
        byte* buffer,
        nuint bufferSize,
        nuint* sizeOut
    )
    {
        return Write(ctx, static req => req._navigator.GetConfig(), buffer, bufferSize, sizeOut);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Load(lua_State* L, void* ctx, byte* path, byte* chunkname, byte* loadname)
    {
        ArgumentNullException.ThrowIfNull(L);
        int topBeforeCallback = lua_gettop(L);
        try
        {
            return LoadCore(L, ctx, path, chunkname, loadname);
        }
        catch (Exception exception)
        {
            lua_settop(L, topBeforeCallback);
            return LuauStateMarshal.ReturnCallbackException(L, "script module loader", exception);
        }
    }

    private static int LoadCore(lua_State* L, void* ctx, byte* path, byte* chunkname, byte* loadname)
    {
        string strPath = ReadUtf8Z(path);
        string strChunkName = ReadUtf8Z(chunkname);
        string strLoadName = ReadUtf8Z(loadname);

        LuauScriptModuleRequirer req = FromVoidPtr(ctx);

        int nResults = 0;
#if DEBUG
        using var guard = new StackGuard(L, expectedDelta: nResults);
#endif

        string? strContent = req._virtualFileSystem.ReadFile(strLoadName);
        if (strContent is null)
        {
            int errorReturn = LuauStateMarshal.ReturnError(L, $"could not read file '{strChunkName}'");
#if DEBUG
            guard.OverwriteExpectedDelta(1);
#endif
            return errorReturn;
        }

        // module needs to run in a new thread, isolated from the rest
        // note: we create ML on main thread so that it doesn't inherit environment of L
        lua_State* GL = lua_mainthread(L);
        lua_State* ML = lua_newthread(GL);
        lua_xmove(GL, L, 1);

        // new thread needs to have the globals sandboxed
        luaL_sandboxthread(ML);

        string? errorMessage = null;
        bool bOk = false;
        ReadOnlySpan<byte> spanSource = Encoding.UTF8.GetBytes(strContent);
        fixed (byte* pSource = spanSource)
        {
            nuint nSizeByteCode = 0;
            byte* pByteCode = luau_compile(pSource, (nuint)spanSource.Length, null, &nSizeByteCode);
            try
            {
                int nStatus = luau_load(ML, chunkname, pByteCode, nSizeByteCode, 0);
                bOk = nStatus == 0;
                if (!bOk)
                {
                    errorMessage =
                        lua_isstring(ML, -1) == 0
                            ? $"unknown error while loading module '{strPath}'"
                            : $"error while loading module '{strPath}': {ReadString(ML, -1)}";
                }
            }
            finally
            {
                luau_free(pByteCode);
            }
        }

        if (bOk)
        {
            int nStatus = lua_resume(ML, L, 0);
            if (nStatus == (int)lua_Status.LUA_OK)
            {
                nResults = lua_gettop(ML);
            }
            else if (nStatus == (int)lua_Status.LUA_YIELD)
            {
                errorMessage = $"module '{strPath}' can not yield";
            }
            else if (lua_isstring(ML, -1) == 0)
            {
                errorMessage = $"unknown error while running module '{strPath}'";
            }
            else
            {
                errorMessage = $"error while running module '{strPath}': {ReadString(ML, -1)}";
            }
        }

        if (errorMessage is null)
            lua_xmove(ML, L, nResults);

        // remove ML thread from L stack
        lua_remove(L, -(nResults + 1));

        if (errorMessage is not null)
        {
            int errorReturn = LuauStateMarshal.ReturnError(L, errorMessage);
#if DEBUG
            guard.OverwriteExpectedDelta(1);
#endif
            return errorReturn;
        }

#if DEBUG
        guard.OverwriteExpectedDelta(nResults);
#endif
        return nResults;
    }

    /// <summary> Writes what <paramref name="read"/> gets from the requirer behind <paramref name="ctx"/>. </summary>
    private static luarequire_WriteResult Write(
        void* ctx,
        Func<LuauScriptModuleRequirer, string?> read,
        byte* bufDest,
        nuint nSizeBufDest,
        nuint* nSizeBufDestOut
    )
    {
        try
        {
            return Write(read(FromVoidPtr(ctx)), bufDest, nSizeBufDest, nSizeBufDestOut);
        }
        catch (Exception)
        {
            return luarequire_WriteResult.WRITE_FAILURE;
        }
    }

    private static luarequire_WriteResult Write(
        string? strSrc,
        byte* bufDest,
        nuint nSizeBufDest,
        nuint* nSizeBufDestOut
    )
    {
        if (strSrc is null)
            return luarequire_WriteResult.WRITE_FAILURE;

        int byteCount = Encoding.UTF8.GetByteCount(strSrc);
        *nSizeBufDestOut = (nuint)byteCount;

        if (bufDest is null || nSizeBufDest < (nuint)byteCount)
            return luarequire_WriteResult.WRITE_BUFFER_TOO_SMALL;

        var buffer = new Span<byte>(bufDest, (int)nSizeBufDest);
        *nSizeBufDestOut = (nuint)Encoding.UTF8.GetBytes(strSrc, buffer);
        return luarequire_WriteResult.WRITE_SUCCESS;
    }

    /// <summary> Reads the string on the stack with its length: an error message may contain NUL bytes. </summary>
    private static string ReadString(lua_State* L, int stackIndex)
    {
        nuint length = 0;
        byte* text = lua_tolstring(L, stackIndex, &length);
        return text is null ? string.Empty : Encoding.UTF8.GetString(text, (int)length);
    }

    private static string ReadUtf8Z(byte* ptr)
    {
        ReadOnlySpan<byte> bytes = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(ptr);
        return Encoding.UTF8.GetString(bytes);
    }

    private void* ToVoidPtr()
    {
        return (void*)GCHandle.ToIntPtr(_handle);
    }

    private static LuauScriptModuleRequirer FromVoidPtr(void* pCtx)
    {
        var gchCtx = GCHandle.FromIntPtr((IntPtr)pCtx);
        return gchCtx.Target as LuauScriptModuleRequirer ?? throw new ArgumentException("invalid context pointer");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_requireContext is not null)
        {
            darp_luau_freerequirecontext(_requireContext);
            _requireContext = null;
        }

        if (_handle.IsAllocated)
            _handle.Free();
    }
}
