using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Internal;
using Darp.Luau.Native;
using Darp.Luau.Utils;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau;

/// <summary>
/// A borrowed view of a Luau value whose type is not known yet: an argument of a managed callback, or the value a
/// script assigns to a userdata member.
/// </summary>
/// <remarks>
/// <para>
/// Read it as the managed type you expect: <c>args[1].Get&lt;int&gt;()</c>. A read that fails throws
/// <see cref="LuaGetException"/>. A callback does not need to catch it: the script gets its message as a Luau
/// error, such as <c>invalid argument #1 (number expected, got string)</c>.
/// </para>
/// <para>
/// The view does not own a registry reference. It is valid only while the callback that received it runs.
/// </para>
/// </remarks>
public readonly unsafe ref struct LuauValueView
{
    private readonly LuauState? _state;
    private readonly lua_State* _luaState;

    // 0 for a value that was not passed.
    private readonly int _stackIndex;

    // The 1-based position of an argument, which names it in an error. 0 for a value that is no argument.
    private readonly int _position;

    internal LuauValueView(LuauState? state, lua_State* luaState, int stackIndex, int position)
    {
        _state = state;
        _luaState = luaState;
        _stackIndex = stackIndex;
        _position = position;
    }

    /// <summary> Gets the type of the value without reading it. <see cref="LuauValueType.Nil"/> for an argument that was not passed. </summary>
    /// <remarks> Use it to decide how to read a value that can have more than one type. </remarks>
    public LuauValueType Type
    {
        get
        {
            _state.ThrowIfDisposed();
            return _stackIndex == 0
                ? LuauValueType.Nil
                : LuauValue.GetValueType((lua_Type)lua_type(_luaState, _stackIndex));
        }
    }

    /// <summary> Gets the name of the type of the value as scripts see it, for error messages: what <c>typeof</c> returns in Luau. </summary>
    public string TypeName => LuauReader.TypeName(GetSource());

    /// <summary> Gets the value as <typeparamref name="T"/>. </summary>
    /// <typeparam name="T">
    /// The managed type to read: a number type, an enum, <see cref="bool"/>, <see cref="string"/>, a byte array, a
    /// managed userdata type, <see cref="LuauValue"/>, an owned reference such as <see cref="LuauTable"/>, a borrowed
    /// view such as <see cref="LuauTableView"/>, or <see cref="ReadOnlySpan{T}"/> of <see cref="byte"/> for the UTF-8
    /// bytes of a string. The nullable form of a number type, an enum, <see cref="bool"/> or an owned reference also
    /// reads nil. <see cref="LuauValue"/> reads nil itself and has no nullable form.
    /// </typeparam>
    /// <returns>
    /// The value. An owned reference is the caller's to dispose. A borrowed view or span is valid while the callback runs.
    /// </returns>
    /// <exception cref="LuaGetException">Thrown when the value is not a <typeparamref name="T"/>.</exception>
    /// <exception cref="NotSupportedException">Thrown when <typeparamref name="T"/> is not a type a Luau value can be read as.</exception>
    public T Get<T>()
        where T : allows ref struct =>
        LuauReader.TryRead<T>(GetSource(), out var value, out ReadFailure failure) ? value : throw ToException(failure);

    /// <summary> Gets the value as <typeparamref name="T"/>, or <c>null</c> when it is nil or was not passed. </summary>
    /// <typeparam name="T">A reference type <see cref="Get{T}"/> reads. For a value type, use its nullable form with <see cref="Get{T}"/>.</typeparam>
    /// <returns>The value, or <c>null</c>.</returns>
    /// <exception cref="LuaGetException">Thrown when the value is neither nil nor a <typeparamref name="T"/>.</exception>
    public T? GetOrNull<T>()
        where T : class
    {
        ReadSource source = GetSource();
        if (source.IsNil)
            return null;
        return LuauReader.TryRead<T>(source, out var value, out ReadFailure failure)
            ? value
            : throw ToException(failure);
    }

    /// <summary> Attempts to get the value as <typeparamref name="T"/>. </summary>
    /// <typeparam name="T">The managed type to read. See <see cref="Get{T}"/>.</typeparam>
    /// <param name="value">The value when it is a <typeparamref name="T"/>.</param>
    /// <returns><c>true</c> when the value is a <typeparamref name="T"/>; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// Use it to choose between the types an argument may have. Nil is read by the types that have a value for it:
    /// a nullable value type, for which <c>TryGet(out int? number)</c> returns <c>true</c> with <c>null</c>, and
    /// <see cref="LuauValue"/>.
    /// </remarks>
    /// <exception cref="NotSupportedException">Thrown when <typeparamref name="T"/> is not a type a Luau value can be read as.</exception>
    public bool TryGet<T>([NotNullWhen(true)] out T? value)
        where T : allows ref struct => TryGet(GetSource(), out value);

    /// <summary> Attempts to get the value as <typeparamref name="T"/>, or as <c>null</c> when it is nil or was not passed. </summary>
    /// <typeparam name="T">A reference type <see cref="Get{T}"/> reads.</typeparam>
    /// <param name="value">The value, or <c>null</c> when it is nil.</param>
    /// <returns><c>true</c> when the value is nil or a <typeparamref name="T"/>; otherwise <c>false</c>.</returns>
    public bool TryGetOrNull<T>(out T? value)
        where T : class
    {
        ReadSource source = GetSource();
        if (!source.IsNil)
            return TryGet(source, out value);

        value = null;
        return true;
    }

    private bool TryGet<T>(scoped in ReadSource source, [NotNullWhen(true)] out T? value)
        where T : allows ref struct
    {
        if (LuauReader.TryRead<T>(source.AsProbe(), out var read, out ReadFailure failure))
        {
            // Not null, except for the null of a nullable value type, which is what the caller asked to read nil as.
            value = read!;
            return true;
        }

        value = default;
        return failure.IsNotSupported ? throw ToException(failure) : false;
    }

    /// <summary> Passes the value on to Luau without reading it. </summary>
    /// <param name="value">The borrowed value. A value that was not passed is nil.</param>
    /// <returns>A temporary representation that is valid while the callback runs.</returns>
    public static implicit operator IntoLuau(LuauValueView value) =>
        value._state is null || value._stackIndex == 0
            ? default
            : IntoLuau.FromRefSource(new StackReference(value._state, value._luaState, value._stackIndex));

    private ReadSource GetSource()
    {
        _state.ThrowIfDisposed();
        return _stackIndex == 0 ? ReadSource.Missing : ReadSource.FromStack(_state, _luaState, _stackIndex);
    }

    /// <summary> The exception of a failed read, worded like Luau words the error of an argument. </summary>
    private Exception ToException(ReadFailure failure)
    {
        if (failure.IsNotSupported)
            return new NotSupportedException(failure.Reason);
        if (_position == 0)
            return new LuaGetException($"invalid value ({failure.Reason})");
        return new LuaGetException(
            _stackIndex == 0
                ? $"missing argument #{_position} ({failure.Reason})"
                : $"invalid argument #{_position} ({failure.Reason})"
        );
    }
}
