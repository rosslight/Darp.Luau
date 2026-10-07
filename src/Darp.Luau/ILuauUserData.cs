namespace Darp.Luau;

/// <summary>
/// Provides callback hooks for userdata behavior exposed to Luau.
/// </summary>
/// <typeparam name="T">Managed userdata type.</typeparam>
public interface ILuauUserData<in T>
    where T : class
{
    /// <summary>
    /// Called when Luau reads a userdata member (<c>__index</c>).
    /// </summary>
    /// <param name="self">Managed userdata instance.</param>
    /// <param name="state">Current Luau state.</param>
    /// <param name="fieldName">Requested member name.</param>
    /// <returns>
    /// Callback result.
    /// Return <see cref="LuauReturnSingle.NotHandled"/> to signal unknown members.
    /// </returns>
    static abstract LuauReturnSingle OnIndex(T self, in LuauState state, in ReadOnlySpan<char> fieldName);

    /// <summary>
    /// Called when Luau assigns a userdata member (<c>__newindex</c>).
    /// </summary>
    /// <param name="self">Managed userdata instance.</param>
    /// <param name="args">View of the assigned Lua value.</param>
    /// <param name="fieldName">Assigned member name.</param>
    /// <returns>
    /// Callback result.
    /// Return <see cref="LuauOutcome.NotHandledError"/> to signal unknown members.
    /// </returns>
    static abstract LuauOutcome OnSetIndex(T self, LuauArgsSingle args, in ReadOnlySpan<char> fieldName);

    /// <summary>
    /// Called when Luau calls a userdata member as a function.
    /// </summary>
    /// <param name="self">Managed userdata instance.</param>
    /// <param name="functionArgs">Method call arguments.</param>
    /// <param name="methodName">Name of the member.</param>
    /// <returns>
    /// Callback result.
    /// Return <see cref="LuauReturn.NotHandledError"/> to signal unknown methods.
    /// Return <see cref="LuauReturn.Await(ValueTask{LuauReturn})"/> to finish later, like a managed function.
    /// </returns>
    static abstract LuauReturn OnMethodCall(T self, LuauArgs functionArgs, in ReadOnlySpan<char> methodName);
}
