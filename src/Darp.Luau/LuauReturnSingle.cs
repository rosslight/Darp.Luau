using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Internal;
using Darp.Luau.Native;

namespace Darp.Luau;

/// <summary>
/// The result of a callback that returns one value, such as a <see cref="LuauGetter{T}"/>.
/// Use <see cref="Ok(IntoLuau)"/> to return a value, or <see cref="Error(string)"/> to report an error.
/// </summary>
/// <remarks>
/// The default value represents an error with message <c>Unknown error</c>.
/// </remarks>
public readonly ref struct LuauReturnSingle
{
    private readonly IntoLuauCopied _value;
    private readonly string? _error;

    /// <summary> Gets whether this callback result is successful. </summary>
    public bool IsOk { get; }

    private LuauReturnSingle(bool isOk, IntoLuau value = default, string? error = null)
    {
        IsOk = isOk;
        _value = value.CaptureCopied();
        _error = error;
    }

    /// <summary> Creates a successful callback result with one value. </summary>
    /// <param name="value">The value to return to Luau.</param>
    public static LuauReturnSingle Ok(IntoLuau value) => new(isOk: true, value: value);

    /// <summary> Creates a failed callback result with an error message. </summary>
    /// <param name="error">Error message reported to the caller.</param>
    /// <remarks>When the provided text is empty or whitespace, <c>Unknown error</c> is used.</remarks>
    public static LuauReturnSingle Error(string error) => new(isOk: false, error: error);

    /// <summary> Pushes a return value when this result is successful. </summary>
    /// <param name="state">Target state that receives the return values.</param>
    /// <param name="error">Receives the error message when this result is not successful.</param>
    /// <returns><c>true</c> when values are available; otherwise <c>false</c>.</returns>
    internal bool TryPushValue(LuauState state, [NotNullWhen(false)] out string? error)
    {
        unsafe
        {
            return TryPushValue(state, state.L, out error);
        }
    }

    internal unsafe bool TryPushValue(LuauState state, lua_State* luaState, [NotNullWhen(false)] out string? error)
    {
        if (!IsOk)
        {
            error = _error ?? "Unknown error";
            return false;
        }
        error = null;
        try
        {
            _value.Push(state, luaState);
            return true;
        }
        finally
        {
            _value.Release();
        }
    }
}
