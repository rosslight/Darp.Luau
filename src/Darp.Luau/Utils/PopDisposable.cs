using Darp.Luau.Native;

namespace Darp.Luau.Utils;

internal readonly ref struct PopDisposable(lua_State* L, bool shouldPop) : IDisposable
{
    private readonly lua_State* _L = L;
    private readonly bool _shouldPop = shouldPop;

    /// <summary> Pops a value from the stack. </summary>
    public void Dispose()
    {
        unsafe
        {
            if (_L is null || !_shouldPop)
                return;
            LuauNative.lua_pop(_L, 1);
        }
    }
}
