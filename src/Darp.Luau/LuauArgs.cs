using System.Diagnostics.CodeAnalysis;
using Darp.Luau.Internal;
using Darp.Luau.Native;
using Darp.Luau.Utils;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau;

/// <summary>
/// Input view used by <see cref="LuauState.CreateFunctionBuilder(LuauState.LuauFunctionBuilder)"/> callbacks.
/// </summary>
public readonly unsafe ref partial struct LuauArgs
{
    private readonly LuauState? _state;
    private readonly lua_State* _luaState;
    private readonly int _firstParameterStackIndex;

    /// <summary> Gets the number of arguments supplied by the Lua caller. </summary>
    public int ArgumentCount { get; }

    /// <summary>
    /// Gets the cancellation token of the async host call that runs this callback, such as
    /// <c>InvokeAsync</c>, <c>ExecuteAsync</c> or <c>ResumeAsync</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="CancellationToken.None"/> outside an async host call. Pass it to the work a callback awaits through
    /// its <see cref="LuauAwaiter"/> and to the async host calls it makes: cancelling it stops the script, but not
    /// the callback, its work, or other host calls.
    /// </remarks>
    public CancellationToken CancellationToken
    {
        get
        {
            _state.ThrowIfDisposed();
            return _state.AsyncDrives.GetCancellationToken(_luaState);
        }
    }

    /// <summary> Asks whether this callback may finish later, and gets the means to do so. </summary>
    /// <param name="awaiter">Turns work into a pending result when the callback may suspend its coroutine.</param>
    /// <param name="error">Why the callback cannot suspend its coroutine here.</param>
    /// <returns><c>true</c> when the callback may suspend its coroutine; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// A callback can only suspend a coroutine that an async host call drives (<c>InvokeAsync</c>,
    /// <c>ExecuteAsync</c> or <c>ResumeAsync</c>), and only where Luau can yield: not inside a metamethod, a
    /// <c>table.sort</c> comparator, or a synchronous <c>Invoke</c> of another callback. Ask before you start the
    /// work, and return <paramref name="error"/> with <see cref="LuauReturn.Error(string)"/> when it is refused.
    /// </remarks>
    public bool TryGetAwaiter(out LuauAwaiter awaiter, [NotNullWhen(false)] out string? error)
    {
        _state.ThrowIfDisposed();
        awaiter = default;
        if (!AsyncDriveTable.IsDriving(_luaState))
        {
            error = CoroutineDriver.AwaitRejectedError;
            return false;
        }

        if (lua_isyieldable(_luaState) == 0)
        {
            error = CoroutineDriver.AwaitNotYieldableError;
            return false;
        }

        awaiter = new LuauAwaiter(isGranted: true);
        error = null;
        return true;
    }

    /// <summary>
    /// Initializes a new argument view over a specific call-frame window.
    /// </summary>
    /// <param name="state">Owning Lua state.</param>
    /// <param name="argumentCount">Number of arguments available in this call frame.</param>
    /// <param name="firstParameterStackIndex">Absolute Lua stack index of parameter <c>1</c>.</param>
    internal LuauArgs(LuauState state, int argumentCount, int firstParameterStackIndex)
        : this(state, state.L, argumentCount, firstParameterStackIndex) { }

    internal LuauArgs(LuauState state, lua_State* luaState, int argumentCount, int firstParameterStackIndex)
    {
        ArgumentNullException.ThrowIfNull(luaState);
        if (!state.OwnsThread(luaState))
            throw new InvalidOperationException("Cross-state callback argument usage is not allowed.");

        _state = state;
        _luaState = luaState;
        ArgumentCount = argumentCount;
        _firstParameterStackIndex = firstParameterStackIndex;
    }

    /// <summary>
    /// Attempts to validate that at least the expected number of arguments were provided.
    /// </summary>
    /// <param name="expectedArgumentCount">Minimum required argument count.</param>
    /// <param name="error">Receives an error message when validation fails.</param>
    /// <returns>
    /// <c>true</c> when <see cref="ArgumentCount"/> is greater than or equal to <paramref name="expectedArgumentCount"/>;
    /// otherwise <c>false</c>.
    /// </returns>
    public bool TryValidateArgumentCount(int expectedArgumentCount, [NotNullWhen(false)] out string? error)
    {
        _state.ThrowIfDisposed();
        if (ArgumentCount >= expectedArgumentCount)
        {
            error = null;
            return true;
        }
        error = $"Expected {expectedArgumentCount} arguments but got {ArgumentCount}.";
        return false;
    }

    /// <summary>
    /// Attempts to read the parameter at <paramref name="parameterIndex"/> as a Lua number.
    /// </summary>
    /// <param name="parameterIndex">1-based parameter index in the range <c>1..ArgumentCount</c>.</param>
    /// <param name="value">Receives the numeric value when the read succeeds.</param>
    /// <param name="error">Receives a descriptive error when the read fails.</param>
    /// <returns>
    /// <c>true</c> when the parameter exists and has type <see cref="lua_Type.LUA_TNUMBER"/>; otherwise <c>false</c>.
    /// </returns>
    public bool TryReadNumber(int parameterIndex, out double value, [NotNullWhen(false)] out string? error)
    {
        value = 0;
        if (!TryGetParameterContext(parameterIndex, out lua_State* L, out int stackIndex, out lua_Type type, out error))
            return false;
        if (!TryRequireType(parameterIndex, type, lua_Type.LUA_TNUMBER, out error))
            return false;

        value = lua_tonumber(L, stackIndex);
        return true;
    }

    /// <summary>
    /// Attempts to read the parameter at <paramref name="parameterIndex"/> as a Lua number or <c>nil</c>.
    /// </summary>
    /// <param name="parameterIndex">1-based parameter index in the range <c>1..ArgumentCount</c>.</param>
    /// <param name="value">
    /// Receives the numeric value when the parameter is a number; receives <c>null</c> when the parameter is <c>nil</c>.
    /// </param>
    /// <param name="error">Receives a descriptive error when the read fails.</param>
    /// <returns>
    /// <c>true</c> when the parameter exists and has type <see cref="lua_Type.LUA_TNUMBER"/> or
    /// <see cref="lua_Type.LUA_TNIL"/>; otherwise <c>false</c>.
    /// </returns>
    public bool TryReadNumberOrNil(int parameterIndex, out double? value, [NotNullWhen(false)] out string? error)
    {
        value = null;
        if (!TryGetParameterContext(parameterIndex, out lua_State* L, out int stackIndex, out lua_Type type, out error))
            return false;
        if (!TryRequireTypeOrNil(parameterIndex, type, lua_Type.LUA_TNUMBER, out bool isNil, out error))
            return false;
        if (isNil)
            return true;

        value = lua_tonumber(L, stackIndex);
        return true;
    }

    /// <summary>
    /// Attempts to read the parameter at <paramref name="parameterIndex"/> as a UTF-8 string.
    /// </summary>
    /// <param name="parameterIndex">1-based parameter index in the range <c>1..ArgumentCount</c>.</param>
    /// <param name="value">Receives a UTF-8 byte span for the parameter value when successful.</param>
    /// <param name="error">Receives a descriptive error when the read fails.</param>
    /// <returns>
    /// <c>true</c> when the parameter exists and has type <see cref="lua_Type.LUA_TSTRING"/>; otherwise <c>false</c>.
    /// </returns>
    /// <remarks>
    /// The resulting span points to Lua owned memory. If a GC cycle is triggered this span might no longer be valid.
    /// </remarks>
    public bool TryReadUtf8String(
        int parameterIndex,
        out ReadOnlySpan<byte> value,
        [NotNullWhen(false)] out string? error
    )
    {
        value = default;
        if (!TryGetParameterContext(parameterIndex, out lua_State* L, out int stackIndex, out lua_Type type, out error))
            return false;
        if (!TryRequireType(parameterIndex, type, lua_Type.LUA_TSTRING, out error))
            return false;

        nuint len = 0;
        byte* pStr = lua_tolstring(L, stackIndex, &len);
        if (pStr is null)
        {
            error = $"Parameter {parameterIndex} returned a null string pointer.";
            return false;
        }

        value = new ReadOnlySpan<byte>(pStr, checked((int)len));
        return true;
    }

    /// <summary>
    /// Attempts to read the parameter at <paramref name="parameterIndex"/> as a UTF-8 string or <c>nil</c>.
    /// </summary>
    /// <param name="parameterIndex">1-based parameter index in the range <c>1..ArgumentCount</c>.</param>
    /// <param name="value">Receives a UTF-8 byte span when the parameter is a string; otherwise <c>default</c>.</param>
    /// <param name="isNil">Set to <c>true</c> when the parameter is <c>nil</c>; otherwise <c>false</c>.</param>
    /// <param name="error">Receives a descriptive error when the read fails.</param>
    /// <returns>
    /// <c>true</c> when the parameter exists and has type <see cref="lua_Type.LUA_TSTRING"/> or
    /// <see cref="lua_Type.LUA_TNIL"/>; otherwise <c>false</c>.
    /// </returns>
    /// <remarks>
    /// The resulting span points to Lua owned memory. If a GC cycle is triggered this span might no longer be valid.
    /// </remarks>
    public bool TryReadUtf8StringOrNil(
        int parameterIndex,
        out ReadOnlySpan<byte> value,
        out bool isNil,
        [NotNullWhen(false)] out string? error
    )
    {
        value = default;
        isNil = false;
        if (!TryGetParameterContext(parameterIndex, out lua_State* L, out int stackIndex, out lua_Type type, out error))
            return false;
        if (!TryRequireTypeOrNil(parameterIndex, type, lua_Type.LUA_TSTRING, out isNil, out error))
            return false;
        if (isNil)
            return true;

        nuint len = 0;
        byte* pStr = lua_tolstring(L, stackIndex, &len);
        if (pStr is null)
        {
            error = $"Parameter {parameterIndex} returned a null string pointer.";
            return false;
        }

        value = new ReadOnlySpan<byte>(pStr, checked((int)len));
        return true;
    }

    /// <summary>
    /// Attempts to read the parameter at <paramref name="parameterIndex"/> as a Lua boolean.
    /// </summary>
    /// <param name="parameterIndex">1-based parameter index in the range <c>1..ArgumentCount</c>.</param>
    /// <param name="value">Receives the boolean value when the read succeeds.</param>
    /// <param name="error">Receives a descriptive error when the read fails.</param>
    /// <returns>
    /// <c>true</c> when the parameter exists and has type <see cref="lua_Type.LUA_TBOOLEAN"/>; otherwise <c>false</c>.
    /// </returns>
    public bool TryReadBoolean(int parameterIndex, out bool value, [NotNullWhen(false)] out string? error)
    {
        value = false;
        if (!TryGetParameterContext(parameterIndex, out lua_State* L, out int stackIndex, out lua_Type type, out error))
            return false;
        if (!TryRequireType(parameterIndex, type, lua_Type.LUA_TBOOLEAN, out error))
            return false;

        value = lua_toboolean(L, stackIndex) == 1;
        return true;
    }

    /// <summary>
    /// Attempts to read the parameter at <paramref name="parameterIndex"/> as a Lua boolean or <c>nil</c>.
    /// </summary>
    /// <param name="parameterIndex">1-based parameter index in the range <c>1..ArgumentCount</c>.</param>
    /// <param name="value">
    /// Receives the boolean value when the parameter is a boolean; receives <c>null</c> when the parameter is <c>nil</c>.
    /// </param>
    /// <param name="error">Receives a descriptive error when the read fails.</param>
    /// <returns>
    /// <c>true</c> when the parameter exists and has type <see cref="lua_Type.LUA_TBOOLEAN"/> or
    /// <see cref="lua_Type.LUA_TNIL"/>; otherwise <c>false</c>.
    /// </returns>
    public bool TryReadBooleanOrNil(int parameterIndex, out bool? value, [NotNullWhen(false)] out string? error)
    {
        value = null;
        if (!TryGetParameterContext(parameterIndex, out lua_State* L, out int stackIndex, out lua_Type type, out error))
            return false;
        if (!TryRequireTypeOrNil(parameterIndex, type, lua_Type.LUA_TBOOLEAN, out bool isNil, out error))
            return false;
        if (isNil)
            return true;

        value = lua_toboolean(L, stackIndex) == 1;
        return true;
    }

    /// <summary>
    /// Attempts to read the parameter at <paramref name="parameterIndex"/> as a Lua buffer.
    /// </summary>
    /// <param name="parameterIndex">1-based parameter index in the range <c>1..ArgumentCount</c>.</param>
    /// <param name="value">Receives a byte span for the buffer contents when successful.</param>
    /// <param name="error">Receives a descriptive error when the read fails.</param>
    /// <returns>
    /// <c>true</c> when the parameter exists and has type <see cref="lua_Type.LUA_TBUFFER"/>; otherwise <c>false</c>.
    /// </returns>
    /// <remarks>
    /// The resulting span points to Lua owned memory.
    /// If a GC cycle is triggered this span might no longer be valid.
    /// </remarks>
    public bool TryReadBuffer(int parameterIndex, out ReadOnlySpan<byte> value, [NotNullWhen(false)] out string? error)
    {
        value = default;
        if (!TryGetParameterContext(parameterIndex, out lua_State* L, out int stackIndex, out lua_Type type, out error))
            return false;
        if (!TryRequireType(parameterIndex, type, lua_Type.LUA_TBUFFER, out error))
            return false;

        nuint nLength = 0;
        void* pBuf = lua_tobuffer(L, stackIndex, &nLength);
        if (pBuf is null)
        {
            error = $"Parameter {parameterIndex} returned a null pointer.";
            return false;
        }

        value = new ReadOnlySpan<byte>(pBuf, checked((int)nLength));
        return true;
    }

    /// <summary>
    /// Attempts to read the parameter at <paramref name="parameterIndex"/> as a Lua buffer or <c>nil</c>.
    /// </summary>
    /// <param name="parameterIndex">1-based parameter index in the range <c>1..ArgumentCount</c>.</param>
    /// <param name="value">Receives a byte span when the parameter is a buffer; otherwise <c>default</c>.</param>
    /// <param name="isNil">Set to <c>true</c> when the parameter is <c>nil</c>; otherwise <c>false</c>.</param>
    /// <param name="error">Receives a descriptive error when the read fails.</param>
    /// <returns>
    /// <c>true</c> when the parameter exists and has type <see cref="lua_Type.LUA_TBUFFER"/> or
    /// <see cref="lua_Type.LUA_TNIL"/>; otherwise <c>false</c>.
    /// </returns>
    /// <remarks>
    /// The resulting span points to Lua owned memory.
    /// If a GC cycle is triggered this span might no longer be valid.
    /// </remarks>
    public bool TryReadBufferOrNil(
        int parameterIndex,
        out ReadOnlySpan<byte> value,
        out bool isNil,
        [NotNullWhen(false)] out string? error
    )
    {
        value = default;
        isNil = false;
        if (!TryGetParameterContext(parameterIndex, out lua_State* L, out int stackIndex, out lua_Type type, out error))
            return false;
        if (!TryRequireTypeOrNil(parameterIndex, type, lua_Type.LUA_TBUFFER, out isNil, out error))
            return false;
        if (isNil)
            return true;

        nuint nLength = 0;
        void* pBuf = lua_tobuffer(L, stackIndex, &nLength);
        if (pBuf is null)
        {
            error = $"Parameter {parameterIndex} returned a null pointer.";
            return false;
        }

        value = new ReadOnlySpan<byte>(pBuf, checked((int)nLength));
        return true;
    }

    /// <summary>
    /// Attempts to read the parameter at <paramref name="parameterIndex"/> as a <see cref="LuauValue"/>.
    /// </summary>
    /// <param name="parameterIndex">1-based parameter index in the range <c>1..ArgumentCount</c>.</param>
    /// <param name="value">Receives the converted <see cref="LuauValue"/> when successful.</param>
    /// <param name="error">Receives a descriptive error when the read fails.</param>
    /// <returns><c>true</c> when the parameter exists and can be converted; otherwise <c>false</c>.</returns>
    public bool TryReadLuauValue(int parameterIndex, out LuauValue value, [NotNullWhen(false)] out string? error)
    {
        value = default;
        _state.ThrowIfDisposed();
        if (!TryGetParameterContext(parameterIndex, out lua_State* L, out int stackIndex, out _, out error))
            return false;

        value = LuauValue.ToValue(_state, L, stackIndex);
        return true;
    }

    /// <summary>
    /// Attempts to read the parameter at <paramref name="parameterIndex"/> as a <see cref="LuauTableView"/>.
    /// </summary>
    /// <param name="parameterIndex">1-based parameter index in the range <c>1..ArgumentCount</c>.</param>
    /// <param name="value">Receives a borrowed <see cref="LuauTableView"/> when successful.</param>
    /// <param name="error">Receives a descriptive error when the read fails.</param>
    /// <returns>
    /// <c>true</c> when the parameter exists and has type <see cref="lua_Type.LUA_TTABLE"/>; otherwise <c>false</c>.
    /// </returns>
    public bool TryReadLuauTable(int parameterIndex, out LuauTableView value, [NotNullWhen(false)] out string? error)
    {
        value = default;
        _state.ThrowIfDisposed();
        if (!TryGetParameterContext(parameterIndex, out lua_State* L, out int stackIndex, out lua_Type type, out error))
            return false;
        if (!TryRequireType(parameterIndex, type, lua_Type.LUA_TTABLE, out error))
            return false;

        value = new LuauTableView(_state, L, stackIndex);
        return true;
    }

    /// <summary>
    /// Attempts to read the parameter at <paramref name="parameterIndex"/> as a <see cref="LuauFunctionView"/>.
    /// </summary>
    /// <param name="parameterIndex">1-based parameter index in the range <c>1..ArgumentCount</c>.</param>
    /// <param name="value">Receives a borrowed <see cref="LuauFunctionView"/> when successful.</param>
    /// <param name="error">Receives a descriptive error when the read fails.</param>
    /// <returns>
    /// <c>true</c> when the parameter exists and has type <see cref="lua_Type.LUA_TFUNCTION"/>; otherwise <c>false</c>.
    /// </returns>
    public bool TryReadLuauFunction(
        int parameterIndex,
        out LuauFunctionView value,
        [NotNullWhen(false)] out string? error
    )
    {
        value = default;
        _state.ThrowIfDisposed();
        if (!TryGetParameterContext(parameterIndex, out lua_State* L, out int stackIndex, out lua_Type type, out error))
            return false;
        if (!TryRequireType(parameterIndex, type, lua_Type.LUA_TFUNCTION, out error))
            return false;

        value = new LuauFunctionView(_state, L, stackIndex);
        return true;
    }

    /// <summary>
    /// Attempts to read the parameter at <paramref name="parameterIndex"/> as a <see cref="LuauCoroutineView"/>.
    /// </summary>
    /// <param name="parameterIndex">1-based parameter index in the range <c>1..ArgumentCount</c>.</param>
    /// <param name="value">Receives a borrowed <see cref="LuauCoroutineView"/> when successful.</param>
    /// <param name="error">Receives a descriptive error when the read fails.</param>
    /// <returns>
    /// <c>true</c> when the parameter exists and has type <see cref="lua_Type.LUA_TTHREAD"/>; otherwise <c>false</c>.
    /// </returns>
    public bool TryReadLuauCoroutine(
        int parameterIndex,
        out LuauCoroutineView value,
        [NotNullWhen(false)] out string? error
    )
    {
        value = default;
        _state.ThrowIfDisposed();
        if (!TryGetParameterContext(parameterIndex, out lua_State* L, out int stackIndex, out lua_Type type, out error))
            return false;
        if (!TryRequireType(parameterIndex, type, lua_Type.LUA_TTHREAD, out error))
            return false;

        value = new LuauCoroutineView(_state, L, stackIndex);
        return true;
    }

    /// <summary>
    /// Attempts to read the parameter at <paramref name="parameterIndex"/> as a <see cref="LuauStringView"/>.
    /// </summary>
    /// <param name="parameterIndex">1-based parameter index in the range <c>1..ArgumentCount</c>.</param>
    /// <param name="value">Receives a borrowed <see cref="LuauStringView"/> when successful.</param>
    /// <param name="error">Receives a descriptive error when the read fails.</param>
    /// <returns>
    /// <c>true</c> when the parameter exists and has type <see cref="lua_Type.LUA_TSTRING"/>; otherwise <c>false</c>.
    /// </returns>
    public bool TryReadLuauString(int parameterIndex, out LuauStringView value, [NotNullWhen(false)] out string? error)
    {
        value = default;
        _state.ThrowIfDisposed();
        if (!TryGetParameterContext(parameterIndex, out lua_State* L, out int stackIndex, out lua_Type type, out error))
            return false;
        if (!TryRequireType(parameterIndex, type, lua_Type.LUA_TSTRING, out error))
            return false;

        value = new LuauStringView(_state, L, stackIndex);
        return true;
    }

    /// <summary>
    /// Attempts to read the parameter at <paramref name="parameterIndex"/> as a <see cref="LuauBufferView"/>.
    /// </summary>
    /// <param name="parameterIndex">1-based parameter index in the range <c>1..ArgumentCount</c>.</param>
    /// <param name="value">Receives a borrowed <see cref="LuauBufferView"/> when successful.</param>
    /// <param name="error">Receives a descriptive error when the read fails.</param>
    /// <returns>
    /// <c>true</c> when the parameter exists and has type <see cref="lua_Type.LUA_TBUFFER"/>; otherwise <c>false</c>.
    /// </returns>
    public bool TryReadLuauBuffer(int parameterIndex, out LuauBufferView value, [NotNullWhen(false)] out string? error)
    {
        value = default;
        _state.ThrowIfDisposed();
        if (!TryGetParameterContext(parameterIndex, out lua_State* L, out int stackIndex, out lua_Type type, out error))
            return false;
        if (!TryRequireType(parameterIndex, type, lua_Type.LUA_TBUFFER, out error))
            return false;

        value = new LuauBufferView(_state, L, stackIndex);
        return true;
    }

    /// <summary>
    /// Attempts to read the parameter at <paramref name="parameterIndex"/> as a <see cref="LuauUserdataView"/>.
    /// </summary>
    /// <param name="parameterIndex">1-based parameter index in the range <c>1..ArgumentCount</c>.</param>
    /// <param name="value">Receives a borrowed <see cref="LuauUserdataView"/> when successful.</param>
    /// <param name="error">Receives a descriptive error when the read fails.</param>
    /// <returns>
    /// <c>true</c> when the parameter exists and has type <see cref="lua_Type.LUA_TUSERDATA"/>; otherwise <c>false</c>.
    /// </returns>
    public bool TryReadLuauUserdata(
        int parameterIndex,
        out LuauUserdataView value,
        [NotNullWhen(false)] out string? error
    )
    {
        value = default;
        _state.ThrowIfDisposed();
        if (!TryGetParameterContext(parameterIndex, out lua_State* L, out int stackIndex, out lua_Type type, out error))
            return false;
        if (!TryRequireType(parameterIndex, type, lua_Type.LUA_TUSERDATA, out error))
            return false;

        value = new LuauUserdataView(_state, L, stackIndex);
        return true;
    }

    /// <summary>
    /// Attempts to read the parameter at <paramref name="parameterIndex"/> as managed userdata of type <typeparamref name="T"/>.
    /// </summary>
    /// <param name="parameterIndex">1-based parameter index in the range <c>1..ArgumentCount</c>.</param>
    /// <param name="value">Receives the managed userdata instance when successful.</param>
    /// <param name="error">Receives a descriptive error when the read fails.</param>
    /// <typeparam name="T">Managed userdata type.</typeparam>
    /// <returns>
    /// <c>true</c> when the parameter exists, is tagged userdata created by this library,
    /// and the userdata payload is of type <typeparamref name="T"/>; otherwise <c>false</c>.
    /// </returns>
    public bool TryReadUserdata<T>(
        int parameterIndex,
        [NotNullWhen(true)] out T? value,
        [NotNullWhen(false)] out string? error
    )
        where T : class, ILuauUserData<T>
    {
        value = null;
        if (!TryGetParameterContext(parameterIndex, out lua_State* L, out int stackIndex, out lua_Type type, out error))
            return false;
        if (!TryRequireType(parameterIndex, type, lua_Type.LUA_TUSERDATA, out error))
            return false;

        return ManagedUserdataResolver.TryResolve(
            L,
            stackIndex,
            out value,
            out error,
            valueLabel: $"Parameter {parameterIndex}"
        );
    }

    /// <summary>
    /// Attempts to read the parameter at <paramref name="parameterIndex"/> as managed userdata of type
    /// <typeparamref name="T"/> or <c>nil</c>.
    /// </summary>
    /// <param name="parameterIndex">1-based parameter index in the range <c>1..ArgumentCount</c>.</param>
    /// <param name="value">Receives the managed userdata instance; receives <c>null</c> when the parameter is <c>nil</c>.</param>
    /// <param name="error">Receives a descriptive error when the read fails.</param>
    /// <typeparam name="T">Managed userdata type.</typeparam>
    /// <returns>
    /// <c>true</c> when the parameter exists and is <c>nil</c>, or when it is userdata of type
    /// <typeparamref name="T"/> created by this library; otherwise <c>false</c>.
    /// </returns>
    public bool TryReadUserdataOrNil<T>(int parameterIndex, out T? value, [NotNullWhen(false)] out string? error)
        where T : class, ILuauUserData<T>
    {
        value = null;
        if (!TryGetParameterContext(parameterIndex, out lua_State* L, out int stackIndex, out lua_Type type, out error))
            return false;
        if (!TryRequireTypeOrNil(parameterIndex, type, lua_Type.LUA_TUSERDATA, out bool isNil, out error))
            return false;
        if (isNil)
            return true;

        return ManagedUserdataResolver.TryResolve(
            L,
            stackIndex,
            out value,
            out error,
            valueLabel: $"Parameter {parameterIndex}"
        );
    }

    /// <summary>
    /// Resolves stack metadata for a parameter index.
    /// </summary>
    /// <param name="parameterIndex">1-based parameter index.</param>
    /// <param name="L">Receives the active Lua state pointer.</param>
    /// <param name="stackIndex">Receives the absolute stack index for the parameter.</param>
    /// <param name="actualType">Receives the parameter's Lua type.</param>
    /// <param name="error">Receives a descriptive error when the index is out of range.</param>
    /// <returns><c>true</c> when the parameter index is valid; otherwise <c>false</c>.</returns>
    private bool TryGetParameterContext(
        int parameterIndex,
        out lua_State* L,
        out int stackIndex,
        out lua_Type actualType,
        [NotNullWhen(false)] out string? error
    )
    {
        L = null;
        stackIndex = 0;
        actualType = default;
        error = null;

        // Throw exceptions for exceptional state. This should not be able to happen
        ArgumentOutOfRangeException.ThrowIfLessThan(parameterIndex, 1);
        _state.ThrowIfDisposed();

        if (parameterIndex > ArgumentCount)
        {
            error = $"Parameter index {parameterIndex} is out of range. Expected 1..{ArgumentCount}.";
            return false;
        }

        stackIndex = _firstParameterStackIndex + parameterIndex - 1;
        L = _luaState;
        actualType = (lua_Type)lua_type(L, stackIndex);
        return true;
    }

    /// <summary>
    /// Validates that the actual Lua type matches the required type.
    /// </summary>
    /// <param name="parameterIndex">1-based parameter index used for error messages.</param>
    /// <param name="actualType">Actual Lua type at the parameter index.</param>
    /// <param name="expectedType">Required Lua type.</param>
    /// <param name="error">Receives a descriptive error when validation fails.</param>
    /// <returns><c>true</c> when <paramref name="actualType"/> equals <paramref name="expectedType"/>.</returns>
    private static bool TryRequireType(
        int parameterIndex,
        lua_Type actualType,
        lua_Type expectedType,
        [NotNullWhen(false)] out string? error
    )
    {
        if (actualType == expectedType)
        {
            error = null;
            return true;
        }

        error =
            actualType == lua_Type.LUA_TNIL
                ? $"Parameter {parameterIndex} is nil but {expectedType} is required."
                : $"Parameter {parameterIndex} must be {expectedType} but was {actualType}.";
        return false;
    }

    /// <summary>
    /// Validates that the actual Lua type matches the required type or is <c>nil</c>.
    /// </summary>
    /// <param name="parameterIndex">1-based parameter index used for error messages.</param>
    /// <param name="actualType">Actual Lua type at the parameter index.</param>
    /// <param name="expectedType">Required non-nil Lua type.</param>
    /// <param name="isNil">Set to <c>true</c> when <paramref name="actualType"/> is <see cref="lua_Type.LUA_TNIL"/>.</param>
    /// <param name="error">Receives a descriptive error when validation fails.</param>
    /// <returns>
    /// <c>true</c> when <paramref name="actualType"/> equals <paramref name="expectedType"/> or
    /// <see cref="lua_Type.LUA_TNIL"/>.
    /// </returns>
    private static bool TryRequireTypeOrNil(
        int parameterIndex,
        lua_Type actualType,
        lua_Type expectedType,
        out bool isNil,
        [NotNullWhen(false)] out string? error
    )
    {
        if (actualType == lua_Type.LUA_TNIL)
        {
            isNil = true;
            error = null;
            return true;
        }

        isNil = false;
        if (actualType == expectedType)
        {
            error = null;
            return true;
        }

        error = $"Parameter {parameterIndex} must be {expectedType} or {lua_Type.LUA_TNIL} but was {actualType}.";
        return false;
    }
}
