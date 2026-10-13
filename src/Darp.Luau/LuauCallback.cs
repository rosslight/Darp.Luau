namespace Darp.Luau;

/// <summary> A managed function that Luau calls: it reads its arguments and returns its results itself. </summary>
/// <param name="args">The arguments of the call.</param>
/// <returns>The results, or work to await.</returns>
/// <remarks>
/// <para>
/// Used by <see cref="LuauState.CreateFunctionManual(LuauCallback)"/>, and for the metamethods and static functions
/// of a userdata type.
/// </para>
/// <para>
/// A callback fails by throwing. The message of the exception becomes the Luau error, which a script can catch
/// with <c>pcall</c>. This holds for every managed code that Luau calls.
/// </para>
/// </remarks>
public delegate LuauReturn LuauCallback(LuauArgs args);

/// <summary> A method of the userdata type <typeparamref name="T"/>. </summary>
/// <param name="self">The instance the method is called on.</param>
/// <param name="args">The arguments of the call, without <paramref name="self"/>.</param>
/// <typeparam name="T">Managed userdata type.</typeparam>
/// <returns>The results, or work to await.</returns>
public delegate LuauReturn LuauMethod<in T>(T self, LuauArgs args)
    where T : class;

/// <summary> Reads a member of the userdata type <typeparamref name="T"/>. </summary>
/// <param name="self">The instance that is read.</param>
/// <param name="state">The state that reads the member.</param>
/// <typeparam name="T">Managed userdata type.</typeparam>
/// <returns>The value of the member.</returns>
public delegate LuauReturnSingle LuauGetter<in T>(T self, LuauState state)
    where T : class;

/// <summary> Writes a member of the userdata type <typeparamref name="T"/>. </summary>
/// <param name="self">The instance that is written.</param>
/// <param name="value">The assigned value.</param>
/// <typeparam name="T">Managed userdata type.</typeparam>
public delegate void LuauSetter<in T>(T self, LuauValueView value)
    where T : class;

/// <summary> Creates a static value of a userdata type for one state. </summary>
/// <param name="state">The state the value is created for.</param>
/// <returns>The value.</returns>
public delegate LuauReturnSingle LuauValueFactory(LuauState state);
