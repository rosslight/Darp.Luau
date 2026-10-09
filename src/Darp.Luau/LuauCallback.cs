namespace Darp.Luau;

/// <summary> A managed function that Luau calls: it reads its arguments and returns its results itself. </summary>
/// <param name="args">The arguments of the call.</param>
/// <returns>The results, an error, or work to await.</returns>
/// <remarks> Used by <see cref="LuauState.CreateFunctionManual(LuauCallback)"/>. </remarks>
public delegate LuauReturn LuauCallback(LuauArgs args);
