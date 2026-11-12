using System.Text;
using Darp.Luau.Native;
using Darp.Luau.Utils;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau;

/// <summary>
/// Represents an exception reported while invoking Luau.
/// </summary>
public sealed class LuaException : Exception
{
    internal LuaException() { }

    internal LuaException(string message)
        : base(message) { }

    internal LuaException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>
    /// Throws a <see cref="LuaException"/> when the Luau status code indicates failure.
    /// </summary>
    /// <param name="L">The state containing the error object on the stack.</param>
    /// <param name="status">The Luau status code returned by the native call.</param>
    /// <param name="description">Additional context for the failing operation.</param>
    /// <exception cref="LuaException">Thrown when <paramref name="status"/> is not successful.</exception>
    public static unsafe void ThrowIfNotOk(lua_State* L, int status, string description)
    {
        unsafe
        {
            if (status == 0)
                return;

#if DEBUG
            using var guard = new StackGuard(L, expectedDelta: -1);
#endif
            nuint outLength = 0;
            byte* err = lua_tolstring(L, -1, &outLength);
            string error = err is null ? "<unknown lua error>" : Encoding.UTF8.GetString(err, (int)outLength);
            lua_pop(L, 1);
            string message = $"Lua invocation {description} failed with status {status}: {error}";
            throw new LuaException(message);
        }
    }
}

/// <summary>
/// Thrown when a Luau value cannot be read as the requested managed type.
/// </summary>
public sealed class LuaGetException : Exception
{
    internal LuaGetException() { }

    internal LuaGetException(string message)
        : base(message) { }

    internal LuaGetException(string message, Exception innerException)
        : base(message, innerException) { }
}
