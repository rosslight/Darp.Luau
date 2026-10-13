using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using Darp.Luau.Internal;
using Darp.Luau.Native;
using Darp.Luau.Utils;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau;

/// <summary> A borrowed view of a Luau table, such as a table argument of a managed callback. </summary>
/// <remarks>
/// <para>
/// The view does not own a registry reference. It is valid only while the callback that received it runs; call
/// <see cref="ToOwned"/> to keep the table.
/// </para>
/// <para>
/// A table is read and written like a script does: with its metamethods. Luau decides what an access does and
/// whether it fails.
/// </para>
/// </remarks>
// The native calls of the success path stand outside of try blocks and using scopes on purpose. On 64-bit targets the
// JIT does not inline a P/Invoke that sits in a protected region and calls it through a stub instead, which costs
// about as much as the access itself. Only pushing the key and the value can throw, so only those are guarded.
public readonly unsafe ref struct LuauTableView
{
    private readonly LuauSlot _slot;

    internal LuauTableView(LuauState state, lua_State* luaState, int stackIndex) =>
        _slot = new LuauSlot(state, luaState, stackIndex);

    internal LuauTableView(LuauSlot slot) => _slot = slot;

    /// <summary> Gets the count of the table if viewed as a list. </summary>
    /// <remarks> If a Luau table has holes, this property is unreliable. </remarks>
    public int ListCount
    {
        get
        {
            LuauState state = _slot.State;
            lua_State* L = state.L;
            _slot.Push(state);
            int count = lua_objlen(L, -1);
            lua_pop(L, 1);
            return count;
        }
    }

    /// <summary> Gets the value for <paramref name="key"/> as <typeparamref name="T"/>. </summary>
    /// <param name="key">The key to look up.</param>
    /// <typeparam name="T">
    /// The managed type to read: a number type, an enum, <see cref="bool"/>, <see cref="string"/>, a byte array, a
    /// managed userdata type, an owned reference such as <see cref="LuauTable"/>, or <see cref="LuauValue"/>. The
    /// nullable form of a number type, an enum, <see cref="bool"/> or an owned reference also reads nil.
    /// <see cref="LuauValue"/> reads nil itself and has no nullable form.
    /// </typeparam>
    /// <returns>The value. An owned reference is the caller's to dispose.</returns>
    /// <remarks> A key the table does not have is nil. </remarks>
    /// <exception cref="LuaGetException">Thrown when the value is not a <typeparamref name="T"/>, or when <c>__index</c> raises an error.</exception>
    /// <exception cref="NotSupportedException">Thrown when <typeparamref name="T"/> is not a type a Luau value can be read as.</exception>
    public T Get<T>(IntoLuau key) =>
        TryRead<T>(key, isProbe: false, out var value, out ReadFailure failure, out _)
            ? value
            : throw ToException(failure);

    /// <summary> Gets the value for <paramref name="key"/> as <typeparamref name="T"/>, or <c>null</c> when it is nil. </summary>
    /// <param name="key">The key to look up.</param>
    /// <typeparam name="T">A reference type <see cref="Get{T}"/> reads. For a value type, use its nullable form with <see cref="Get{T}"/>.</typeparam>
    /// <returns>The value, or <c>null</c>.</returns>
    /// <exception cref="LuaGetException">Thrown when the value is neither nil nor a <typeparamref name="T"/>, or when <c>__index</c> raises an error.</exception>
    public T? GetOrNull<T>(IntoLuau key)
        where T : class
    {
        if (TryRead<T>(key, isProbe: false, out var value, out ReadFailure failure, out bool isNil))
            return value;
        return isNil ? null : throw ToException(failure);
    }

    /// <summary> Attempts to get the value for <paramref name="key"/> as <typeparamref name="T"/>. </summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="value">The value when it is a <typeparamref name="T"/>.</param>
    /// <typeparam name="T">The managed type to read. See <see cref="Get{T}"/>.</typeparam>
    /// <returns>
    /// <c>true</c> when the value is a <typeparamref name="T"/>; <c>false</c> when it is not, or when
    /// <c>__index</c> raises an error.
    /// </returns>
    /// <remarks>
    /// Nil is read by the types that have a value for it: a nullable value type, for which
    /// <c>TryGet(key, out int? number)</c> returns <c>true</c> with <c>null</c>, and <see cref="LuauValue"/>.
    /// </remarks>
    /// <exception cref="NotSupportedException">Thrown when <typeparamref name="T"/> is not a type a Luau value can be read as.</exception>
    public bool TryGet<T>(IntoLuau key, [NotNullWhen(true)] out T? value)
    {
        if (TryRead<T>(key, isProbe: true, out var read, out ReadFailure failure, out _))
        {
            // Not null, except for the null of a nullable value type, which is what the caller asked to read nil as.
            value = read!;
            return true;
        }

        value = default;
        return failure.IsNotSupported ? throw ToException(failure) : false;
    }

    /// <summary> Attempts to get the value for <paramref name="key"/> as <typeparamref name="T"/>, or as <c>null</c> when it is nil. </summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="value">The value, or <c>null</c> when it is nil.</param>
    /// <typeparam name="T">A reference type <see cref="Get{T}"/> reads.</typeparam>
    /// <returns>
    /// <c>true</c> when the value is nil or a <typeparamref name="T"/>; <c>false</c> when it is another type, or
    /// when <c>__index</c> raises an error.
    /// </returns>
    public bool TryGetOrNull<T>(IntoLuau key, out T? value)
        where T : class
    {
        if (TryRead<T>(key, isProbe: true, out var read, out _, out bool isNil))
        {
            value = read;
            return true;
        }

        value = null;
        return isNil;
    }

    /// <summary> Gets the value for <paramref name="key"/>, or a nil value when the table has none. </summary>
    /// <param name="key">The key to look up.</param>
    /// <remarks> Also a nil value when <c>__index</c> raises an error. Dispose the value when it may be reference-backed. </remarks>
    [SuppressMessage(
        "Design",
        "CA1043:Use integral or string argument for indexers",
        Justification = "Lua tables support arbitrary key types, and IntoLuau models that domain directly."
    )]
    public LuauValue this[IntoLuau key] => TryGet(key, out LuauValue value) ? value : default;

    /// <summary> Sets the value for <paramref name="key"/>. </summary>
    /// <param name="key">The key to set.</param>
    /// <param name="value">The value to set. Nil removes the key.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is nil.</exception>
    /// <exception cref="LuaException">Thrown when Luau rejects the write, for example when <c>__newindex</c> raises an error or the table is frozen.</exception>
    public void Set(IntoLuau key, IntoLuau value)
    {
        LuauState state = _slot.State;
        if (key.Type is IntoLuau.Kind.Nil)
            throw new ArgumentNullException(nameof(key), "Cannot set a table value with nil key");
        lua_State* L = state.L;
#if DEBUG
        using var guard = new StackGuard(L, expectedDelta: 0);
#endif
        _slot.Push(state); // [table]
        PushOrPop(state, L, key, pushedCount: 1); // [table, key]
        PushOrPop(state, L, value, pushedCount: 2); // [table, key, value]

        int status = LuauVm.SetTable(state, L, -3); // [table] or [table, error]
        if (status != (int)lua_Status.LUA_OK)
        {
            string error = PopErrorMessage(L);
            lua_pop(L, 1);
            throw new LuaException($"Lua invocation lua_settable failed with status {status}: {error}");
        }
        lua_pop(L, 1);
    }

    /// <summary> Determines whether <paramref name="key"/> resolves to a value that is not nil. </summary>
    /// <param name="key">The key to look up.</param>
    /// <remarks> Uses regular table lookup and therefore honors table metamethods such as <c>__index</c>. </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is nil.</exception>
    /// <exception cref="LuaException">Thrown when <c>__index</c> raises an error.</exception>
    public bool ContainsKey(IntoLuau key)
    {
        LuauState state = _slot.State;
        if (key.Type is IntoLuau.Kind.Nil)
            throw new ArgumentNullException(nameof(key), "Cannot look up a table value with nil key");
        lua_State* L = state.L;
        if (!TryPushValue(state, L, key, out lua_Type type))
            throw new LuaException($"Could not get the table value: {PopError(L)}");
        lua_pop(L, 2);
        return type != lua_Type.LUA_TNIL;
    }

    /// <summary> Creates an owned reference to the table, which stays valid after the callback. </summary>
    /// <returns>The owned reference, which the caller disposes.</returns>
    public LuauTable ToOwned() => new(_slot.State, _slot.ToOwnedHandle());

    /// <summary> Passes the table on to Luau without creating an owned reference. </summary>
    /// <param name="value">The borrowed table.</param>
    /// <returns>A temporary representation that is valid as long as the view.</returns>
    public static implicit operator IntoLuau(LuauTableView value) => IntoLuau.FromSlot(value._slot);

    /// <inheritdoc />
    public override string ToString() => _slot.ToString();

    /// <summary> The UTF-8 bytes of the string at <paramref name="key"/>, which Luau owns. </summary>
    /// <remarks> Nothing keeps the bytes alive once this method returns. See <see cref="LuauMarshal"/>. </remarks>
    internal bool TryGetStringBytes(scoped in IntoLuau key, out ReadOnlySpan<byte> bytes, out bool isNil) =>
        TryGetBytes(key, lua_Type.LUA_TSTRING, out bytes, out isNil);

    /// <summary> The bytes of the buffer at <paramref name="key"/>, which Luau owns. </summary>
    /// <remarks> Nothing keeps the bytes alive once this method returns. See <see cref="LuauMarshal"/>. </remarks>
    internal bool TryGetBufferBytes(scoped in IntoLuau key, out ReadOnlySpan<byte> bytes, out bool isNil) =>
        TryGetBytes(key, lua_Type.LUA_TBUFFER, out bytes, out isNil);

    private bool TryGetBytes(
        scoped in IntoLuau key,
        lua_Type expectedType,
        out ReadOnlySpan<byte> bytes,
        out bool isNil
    )
    {
        bytes = default;
        isNil = false;
        LuauState state = _slot.State;
        lua_State* L = state.L;
        if (!TryPushValue(state, L, key, out lua_Type type))
        {
            lua_pop(L, 2);
            return false;
        }

        isNil = type is lua_Type.LUA_TNIL;
        if (type == expectedType)
        {
            bytes =
                expectedType is lua_Type.LUA_TSTRING ? LuauReader.StringBytes(L, -1) : LuauReader.BufferBytes(L, -1);
        }
        lua_pop(L, 2);
        return type == expectedType;
    }

    /// <summary> Reads the value at <paramref name="key"/> as <typeparamref name="T"/> and leaves the stack as it was. </summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="isProbe">Whether the read only asks if the value is a <typeparamref name="T"/>. See <see cref="ReadSource.IsProbe"/>.</param>
    /// <param name="value">The value when it was read.</param>
    /// <param name="failure">Why the value was not read.</param>
    /// <param name="isNil">Whether the value is nil, also when a <typeparamref name="T"/> that has a value for nil read it.</param>
    private bool TryRead<T>(
        scoped in IntoLuau key,
        bool isProbe,
        [MaybeNullWhen(false)] out T value,
        out ReadFailure failure,
        out bool isNil
    )
    {
        LuauState state = _slot.State;
        lua_State* L = state.L;
        if (!TryPushValue(state, L, key, out lua_Type type))
        {
            value = default;
            isNil = false;
            if (isProbe)
            {
                // Nobody looks at why a probe failed, so the error is not turned into text for it.
                lua_pop(L, 2);
                failure = default;
            }
            else
            {
                failure = ReadFailure.Error(PopError(L));
            }
            return false;
        }

        isNil = type is lua_Type.LUA_TNIL;
        // The reader reports what it finds instead of throwing, so the two values are popped on every path.
        ReadSource source = ReadSource.FromStack(state, L, -1, type);
        bool isRead = LuauReader.TryRead(isProbe ? source.AsProbe() : source, out value, out failure);
        lua_pop(L, 2);
        return isRead;
    }

    /// <summary> Pushes the table and the value at <paramref name="key"/>. </summary>
    /// <returns>
    /// <c>false</c> when <c>__index</c> raised an error. The error object is then above the table instead of the
    /// value, and the caller pops both as well.
    /// </returns>
    /// <remarks> Inlined into the reads, so that a read is one call into the VM. </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryPushValue(LuauState state, lua_State* L, scoped in IntoLuau key, out lua_Type type)
    {
        _slot.Push(state); // [table]
        PushOrPop(state, L, key, pushedCount: 1); // [table, key]

        int result = LuauVm.GetTable(state, L, -2); // [table, value] or [table, error]
        type = (lua_Type)result;
        return result >= 0;
    }

    /// <summary> Reads the error object above the table as text and pops both. </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string PopError(lua_State* L)
    {
        string error = PopErrorMessage(L);
        lua_pop(L, 1);
        return error;
    }

    /// <summary>
    /// Pushes <paramref name="value"/>. When that throws, pops the <paramref name="pushedCount"/> values the caller
    /// has pushed before, so that a failed access leaves the stack as it found it.
    /// </summary>
    private static void PushOrPop(LuauState state, lua_State* L, scoped in IntoLuau value, int pushedCount)
    {
        try
        {
            value.Push(state);
        }
        catch
        {
            lua_pop(L, pushedCount);
            throw;
        }
    }

    /// <summary> Reads the error object on top of the stack as text and pops it. </summary>
    private static string PopErrorMessage(lua_State* L)
    {
        nuint length = 0;
        byte* message = lua_tolstring(L, -1, &length);
        string error = message is null ? "<unknown lua error>" : Encoding.UTF8.GetString(message, (int)length);
        lua_pop(L, 1);
        return error;
    }

    private static Exception ToException(ReadFailure failure) => failure.ToException("table value");
}
