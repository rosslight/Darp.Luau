#if DEBUG
using System.Runtime.CompilerServices;
using System.Text;
using Darp.Luau.Native;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Utils;

internal ref struct StackGuard(lua_State* l, int expectedDelta = 0, [CallerMemberName] string? callerName = null)
    : IDisposable
{
    private static Exception? s_exceptionInFlight;

    private readonly lua_State* _L = l;
    private readonly int _startTop = unsafe(lua_gettop(l));
    private int _expectedDelta = expectedDelta;
    private readonly string? _callerName = callerName;

    public void OverwriteExpectedDelta(int newExpectedDelta) => _expectedDelta = newExpectedDelta;

    public void Dispose()
    {
        unsafe
        {
            int now = lua_gettop(_L);
            int expected = _startTop + _expectedDelta;

            if (now == expected)
                return;

            string direction = now > _startTop ? "grown" : "decreased";
            s_exceptionInFlight = _expectedDelta switch
            {
                0 => new InvalidOperationException(
                    $"Lua stack mismatch at {_callerName}: Stack has {direction} from {_startTop} to {now} but should be the same\n{StackDump()}",
                    s_exceptionInFlight
                ),
                _ => new InvalidOperationException(
                    $"Lua stack mismatch at {_callerName}: Stack has {direction} by {now - _startTop} from {_startTop} to {now} but should have by {_expectedDelta} to {_startTop + _expectedDelta}\n{StackDump()}",
                    s_exceptionInFlight
                ),
            };
            throw s_exceptionInFlight;
        }
    }

    private string StackDump()
    {
        unsafe
        {
            using var writer = new StringWriter();
            writer.WriteLine("Lua stack:");
            int top = lua_gettop(_L);
            for (int i = 1; i <= top; i++)
            {
                var t = (lua_Type)lua_type(_L, i);
                switch (t)
                {
                    case lua_Type.LUA_TBOOLEAN:
                        writer.WriteLine($"  [{i}]: {lua_toboolean(_L, i)}");
                        break;
                    case lua_Type.LUA_TSTRING:
                        nuint lenStr = 0;
                        byte* pStr = lua_tolstring(_L, i, &lenStr);
                        writer.WriteLine($"  [{i}]: {Encoding.UTF8.GetString(pStr, (int)lenStr)}");
                        break;
                    case lua_Type.LUA_TNUMBER:
                        writer.WriteLine($"  [{i}]: {lua_tonumber(_L, i)}");
                        break;
                    default:
                        byte* pType = lua_typename(_L, (int)t);
                        int lenType = 0;
                        for (; ; )
                        {
                            if (pType is null || pType[lenType] == 0)
                                break;
                            lenType++;
                        }
                        writer.WriteLine($"  [{i}]: {Encoding.UTF8.GetString(pType, lenType)}");
                        break;
                }
            }
            return writer.ToString();
        }
    }
}

#endif
