using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Darp.Luau.Native;
using Darp.Luau.Utils;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Internal.Require;

internal sealed unsafe class LuauModuleRequirer : IDisposable
{
    private readonly LuauState _state;
    private readonly LuauHostModuleRequirer _hostModules;
    private readonly LuauScriptModuleRequirer _scriptModules;
    private LuauFunction _proxyRequire;
    private GCHandle _handle;

    public LuauModuleRequirer(LuauState state, ILuauFileSystem virtualFileSystem)
    {
        _state = state;
        _hostModules = new LuauHostModuleRequirer(state);
        _scriptModules = new LuauScriptModuleRequirer(state, virtualFileSystem);
        _handle = GCHandle.Alloc(this);

        try
        {
            InstallRequireFunction();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void EnableScriptModules()
    {
        _scriptModules.Enabled = true;
    }

    public void RegisterHostModule(string name, LuauState.OnModuleLoad onLoad)
    {
        _hostModules.RegisterModule(name, onLoad);
    }

    private void InstallRequireFunction()
    {
#if DEBUG
        using var guard = new StackGuard(_state.L, expectedDelta: 0);
#endif
        _proxyRequire = _scriptModules.CreateProxyRequireFunction(_state);
        ulong proxyRequireHandle = _proxyRequire.GetHandleOrThrow();
        RegistryReferenceTracker.TrackedReference proxyRequireReference = _state.GetTrackedReferenceOrThrow(
            proxyRequireHandle
        );

        lua_State* L = _state.L;
        LuauStateMarshal.PushString(L, "require"u8); // [name]
#pragma warning disable CA2000 // The native require callback captures this stack value as an upvalue.
        _ = proxyRequireReference.PushToTop(); // [name, proxy]
#pragma warning restore CA2000
        fixed (byte* pRequireName = "require\0"u8)
            darp_luau_pushrequirecallback(L, &RequireCallback, ToVoidPtr(), pRequireName); // [name, require]
        // The globals may be frozen or have metamethods by now, so Luau decides whether this works.
        LuaException.ThrowIfNotOk(L, LuauVm.SetTable(_state, L, LUA_GLOBALSINDEX), "lua_settable");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int RequireCallback(lua_State* L, void* ctx)
    {
        ArgumentNullException.ThrowIfNull(L);
        int topBeforeCallback = lua_gettop(L);
        try
        {
            return RequireCallbackCore(L, ctx);
        }
        catch (Exception exception)
        {
            lua_settop(L, topBeforeCallback);
            return LuauStateMarshal.ReturnCallbackException(L, "require", exception);
        }
    }

    private static int RequireCallbackCore(lua_State* L, void* ctx)
    {
        LuauModuleRequirer requirer = FromVoidPtr(ctx);
        if (!LuauStateMarshal.TryGetString(L, 1, out ReadOnlySpan<byte> utf8ModuleName))
            return LuauStateMarshal.ReturnError(L, "bad argument #1 to 'require' (string expected)");

        string moduleName = Encoding.UTF8.GetString(utf8ModuleName);
        RequireResolution resolution = requirer.Resolve(moduleName);
        switch (resolution.Kind)
        {
            case RequireResolutionKind.HostModule:
                requirer.PushModule(L, resolution.Module);
                return 1;
            case RequireResolutionKind.ScriptModule:
                lua_settop(L, 1);
                LuauStateMarshal.PushString(L, GetCurrentFile(L));
                return DARP_LUAU_REQUIRE_PROXY;
            case RequireResolutionKind.Error:
                return LuauStateMarshal.ReturnError(L, resolution.Error ?? "require failed");
            case RequireResolutionKind.NotFound:
            default:
                return LuauStateMarshal.ReturnError(L, $"module '{moduleName}' is not registered");
        }
    }

    private RequireResolution Resolve(string moduleName)
    {
        RequireResolution hostResolution = _hostModules.Resolve(moduleName);
        if (hostResolution.Kind != RequireResolutionKind.NotFound)
            return hostResolution;

        return _scriptModules.Resolve(moduleName);
    }

    private void PushModule(lua_State* L, LuauTable module)
    {
        ulong handle = module.GetHandleOrThrow();
        RegistryReferenceTracker.TrackedReference trackedReference = _state.GetTrackedReferenceOrThrow(handle);
#if DEBUG
        using var guard = new StackGuard(_state.L, expectedDelta: (nint)L == (nint)_state.L ? 1 : 0);
#endif
#pragma warning disable CA2000 // The pushed value is returned to the caller stack.
        _ = trackedReference.PushToTop();
#pragma warning restore CA2000
        if ((nint)L != (nint)_state.L)
            lua_xmove(_state.L, L, 1);
    }

    public void Dispose()
    {
        _proxyRequire.Dispose();
        _hostModules.Dispose();
        _scriptModules.Dispose();

        if (_handle.IsAllocated)
            _handle.Free();
    }

    private void* ToVoidPtr()
    {
        return (void*)GCHandle.ToIntPtr(_handle);
    }

    private static LuauModuleRequirer FromVoidPtr(void* context)
    {
        var handle = GCHandle.FromIntPtr((IntPtr)context);
        return handle.Target as LuauModuleRequirer ?? throw new ArgumentException("invalid context pointer");
    }

    private static string GetCurrentFile(lua_State* L)
    {
        var debug = new lua_Debug();
        for (int level = 1; ; level++)
        {
            int status;
            fixed (byte* pWhat = "s\0"u8)
            {
                status = lua_getinfo(L, level, pWhat, &debug);
            }

            if (status == 0)
                return "=stdin";

            if (debug.what is null || !NullTerminatedEquals(debug.what, "C"u8))
            {
                if (debug.source is null)
                    return "=stdin";

                return ReadUtf8Z(debug.source);
            }
        }
    }

    private static string ReadUtf8Z(byte* ptr)
    {
        ReadOnlySpan<byte> bytes = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(ptr);
        return Encoding.UTF8.GetString(bytes);
    }

    private static bool NullTerminatedEquals(byte* value, ReadOnlySpan<byte> expected)
    {
        for (int i = 0; i < expected.Length; i++)
        {
            if (value[i] != expected[i])
                return false;
        }

        return value[expected.Length] == 0;
    }
}

internal enum RequireResolutionKind
{
    NotFound = 0,
    HostModule = 1,
    ScriptModule = 2,
    Error = 3,
}

internal readonly record struct RequireResolution(RequireResolutionKind Kind, LuauTable Module, string? Error)
{
    public static RequireResolution NotFound { get; } = new(RequireResolutionKind.NotFound, default, null);

    public static RequireResolution ScriptModule { get; } = new(RequireResolutionKind.ScriptModule, default, null);

    public static RequireResolution LoadedHostModule(LuauTable module) =>
        new(RequireResolutionKind.HostModule, module, null);

    public static RequireResolution LoadError(string error) => new(RequireResolutionKind.Error, default, error);
}
