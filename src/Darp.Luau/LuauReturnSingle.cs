using Darp.Luau.Internal;
using Darp.Luau.Native;

namespace Darp.Luau;

/// <summary>
/// The result of a callback that returns one value, such as a <see cref="LuauGetter{T}"/>.
/// Use <see cref="Ok(IntoLuau)"/> to return the value.
/// </summary>
/// <remarks>
/// <para>
/// The value is captured when the result is created. A callback can therefore return a reference it created and
/// dispose its own: <c>using LuauTable table = ...; return LuauReturnSingle.Ok(table);</c>.
/// </para>
/// <para>
/// A callback fails by throwing: the message of its exception becomes the Luau error. The default value returns
/// nil.
/// </para>
/// </remarks>
public readonly ref struct LuauReturnSingle
{
    private readonly IntoLuauCopied _value;

    private LuauReturnSingle(IntoLuau value) => _value = value.CaptureCopied();

    /// <summary> Creates a callback result with one value. </summary>
    /// <param name="value">The value to return to Luau.</param>
    public static LuauReturnSingle Ok(IntoLuau value) => new(value);

    /// <summary> Pushes the value and releases what the result captured. </summary>
    /// <param name="state">The state that receives the value.</param>
    /// <param name="luaState">The stack the value is pushed onto.</param>
    internal unsafe void Push(LuauState state, lua_State* luaState)
    {
        try
        {
            _value.Push(state, luaState);
        }
        finally
        {
            _value.Release();
        }
    }
}
