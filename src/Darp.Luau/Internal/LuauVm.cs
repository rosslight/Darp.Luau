using System.Diagnostics;
using System.Runtime.CompilerServices;
using Darp.Luau.Native;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Internal;

/// <summary>
/// The only place where the host enters the Luau VM in a way that can run script code, and the only place that
/// closes it.
/// </summary>
/// <remarks>
/// <para>
/// Two rules hold because every such call goes through here. The native functions behind these methods are banned
/// everywhere else in this assembly (<c>BannedSymbols.txt</c>), so a new entry cannot bypass them.
/// </para>
/// <para>
/// No Luau error unwinds through managed frames: each method calls a native function that returns the error as a
/// status. An unprotected call such as <c>lua_gettable</c> would raise it through the caller instead.
/// </para>
/// <para>
/// The state is never closed while it runs: each method counts itself in <see cref="LuauState.VmDepth"/>. Host code
/// can only run above Luau frames while one of these calls is in progress, whatever kind of callback it is, so
/// <see cref="LuauState.Dispose"/> checks that count. There is no <c>finally</c>: no managed exception passes
/// through a native call, because the callbacks of this library catch everything before they return to Luau.
/// </para>
/// </remarks>
#pragma warning disable RS0030 // Banned API: this is the file the ban points to.
internal static unsafe class LuauVm
{
    /// <summary> Calls the function below its <paramref name="nargs"/> arguments. </summary>
    /// <returns>The status. On an error, the error object replaces the function and its arguments.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int PCall(LuauState state, lua_State* L, int nargs, int nresults)
    {
        state.VmDepth++;
        int status = lua_pcall(L, nargs, nresults, 0);
        state.VmDepth--;
        return status;
    }

    /// <summary> Loads bytecode as a function. </summary>
    /// <remarks>
    /// Into a sandboxed environment, loading resolves the imports of the chunk, such as <c>math.floor</c>, by
    /// reading globals. A script can give those an <c>__index</c>, so loading can run script code and callbacks.
    /// </remarks>
    /// <returns>The status. The function, or the error message, is pushed.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Load(LuauState state, lua_State* L, byte* chunkName, byte* byteCode, nuint size, int env)
    {
        state.VmDepth++;
        int status = luau_load(L, chunkName, byteCode, size, env);
        state.VmDepth--;
        return status;
    }

    /// <summary> Starts or continues <paramref name="coroutine"/> with its top <paramref name="narg"/> values. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Resume(LuauState state, lua_State* coroutine, lua_State* from, int narg)
    {
        state.VmDepth++;
        int status = lua_resume(coroutine, from, narg);
        state.VmDepth--;
        return status;
    }

    /// <summary> Continues a coroutine that waits in a managed callback. Its top values become the results. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ResumeCallback(LuauState state, lua_State* coroutine, int narg)
    {
        state.VmDepth++;
        int status = darp_luau_resumecallback(coroutine, null, narg);
        state.VmDepth--;
        return status;
    }

    /// <summary> Continues a suspended coroutine by raising the error on top of its stack where it waits. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ResumeError(LuauState state, lua_State* coroutine)
    {
        state.VmDepth++;
        int status = lua_resumeerror(coroutine, null);
        state.VmDepth--;
        return status;
    }

    /// <summary> Replaces the key on top with <c>t[key]</c> for the table at <paramref name="idx"/>. </summary>
    /// <returns>The type of the value, or the negated status with the error object in place of the key.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetTable(LuauState state, lua_State* L, int idx)
    {
        state.VmDepth++;
        int result = darp_luau_pgettable(L, idx);
        state.VmDepth--;
        return result;
    }

    /// <summary> Does <c>t[key] = value</c> for the table at <paramref name="idx"/> and pops key and value. </summary>
    /// <returns>The status. On an error, the error object replaces key and value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SetTable(LuauState state, lua_State* L, int idx)
    {
        state.VmDepth++;
        int status = darp_luau_psettable(L, idx);
        state.VmDepth--;
        return status;
    }

    /// <summary> Closes the VM. The caller has checked that it does not run. </summary>
    public static void Close(LuauState state)
    {
        Debug.Assert(state.VmDepth == 0);
        lua_close(state.L);
    }
}
