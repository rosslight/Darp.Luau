using System.Buffers;
using System.Text;
using Darp.Luau.Native;
using Darp.Luau.Utils;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Internal.Async;

internal sealed unsafe class LuauAsyncChunkSource
{
    private readonly LuauCompiler _compiler;
    private readonly string? _charSource;
    private readonly byte[]? _utf8Source;
    private readonly string _chunkName;
    private readonly ulong _environmentHandle;
    private readonly bool _releaseEnvironmentHandle;

    public LuauAsyncChunkSource(
        LuauState state,
        LuauCompiler compiler,
        LuauChunkSourceKind sourceKind,
        ReadOnlySpan<char> charSource,
        ReadOnlySpan<byte> utf8Source,
        ReadOnlySpan<char> chunkName,
        ulong environmentHandle
    )
    {
        _compiler = compiler;
        _charSource = sourceKind == LuauChunkSourceKind.Chars ? charSource.ToString() : null;
        _utf8Source = sourceKind == LuauChunkSourceKind.Utf8Bytes ? utf8Source.ToArray() : null;
        _chunkName = chunkName.ToString();
        if (environmentHandle != 0)
        {
            _environmentHandle = state.ReferenceTracker.CountRefOrThrow(environmentHandle);
            _releaseEnvironmentHandle = true;
        }
    }

    public void LoadCompiledChunk(LuauState state, lua_State* thread)
    {
        string chunkName = string.IsNullOrEmpty(_chunkName) ? "=main" : _chunkName;
        byte[] chunkNameBuffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetByteCount(chunkName) + 1);
        try
        {
            int actualChunkNameBytes = Encoding.UTF8.GetBytes(chunkName, chunkNameBuffer);
            chunkNameBuffer[actualChunkNameBytes] = 0;
            fixed (byte* pChunkName = chunkNameBuffer)
            {
                if (_utf8Source is not null)
                {
                    fixed (byte* pSource = _utf8Source)
                    {
                        CompileAndLoadByteCode(state, thread, pSource, (nuint)_utf8Source.Length, pChunkName);
                    }
                    return;
                }

                string source = _charSource ?? string.Empty;
                byte[] sourceBuffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetByteCount(source));
                try
                {
                    int actualSourceBytes = Encoding.UTF8.GetBytes(source, sourceBuffer);
                    fixed (byte* pSource = sourceBuffer)
                    {
                        CompileAndLoadByteCode(state, thread, pSource, (nuint)actualSourceBytes, pChunkName);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(sourceBuffer);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunkNameBuffer);
        }
    }

    private void CompileAndLoadByteCode(LuauState state, lua_State* thread, byte* pSource, nuint sourceLength, byte* pChunkName)
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
                RegistryReferenceTracker.TrackedReference environmentReference = state.GetTrackedReferenceOrThrow(
                    _environmentHandle
                );
#pragma warning disable CA2000
                _ = environmentReference.PushToTop();
#pragma warning restore CA2000
                if ((nint)state.L != (nint)thread)
                    lua_xmove(state.L, thread, 1);
                environmentStackIndex = lua_gettop(thread);
            }
            try
            {
                int loadStatus = luau_load(thread, pChunkName, pByteCode, nSizeByteCode, environmentStackIndex);
                LuaException.ThrowIfNotOk(thread, loadStatus, "luau_load");
            }
            finally
            {
                if (environmentStackIndex != 0)
                    lua_remove(thread, environmentStackIndex);
            }
        }
        finally
        {
            luau_free(pByteCode);
        }
    }

    public void Release(LuauState state)
    {
        if (_releaseEnvironmentHandle)
            state.ReferenceTracker.ReleaseRef(_environmentHandle);
    }
}
