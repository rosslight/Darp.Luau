namespace Darp.Luau;

/// <summary>
/// Reads memory that Luau owns without copying it. Use it where the copy of the regular getters is too expensive.
/// </summary>
/// <remarks>
/// <para>
/// A span returned here points into the Luau heap. Nothing on the managed side keeps that memory alive: it is valid
/// only while the table still holds the value. When a script or the host replaces or removes the value, the next
/// garbage collection of Luau can free the memory, and reading the span afterwards is undefined behavior.
/// </para>
/// <para>
/// Use the span at once, before any script runs and before the table is written, and copy what you want to keep.
/// The regular getters, such as <see cref="LuauTable.GetUtf8String"/> and <see cref="LuauTable.GetBuffer"/>, return
/// copies and have no such rule.
/// </para>
/// </remarks>
public static class LuauMarshal
{
    /// <summary> Attempts to get the UTF-8 bytes of the string at <paramref name="key"/> without copying them. </summary>
    /// <param name="table">The table to read from.</param>
    /// <param name="key">Table key to resolve.</param>
    /// <param name="value">The bytes of the string, which Luau owns.</param>
    /// <returns><c>true</c> when the value exists and is a string; otherwise <c>false</c>.</returns>
    public static bool TryGetUtf8StringSpan(in LuauTable table, IntoLuau key, out ReadOnlySpan<byte> value) =>
        table.TryGetUtf8String(key, out value, out _);

    /// <summary> Attempts to get the UTF-8 bytes of the string or <c>nil</c> at <paramref name="key"/> without copying them. </summary>
    /// <param name="table">The table to read from.</param>
    /// <param name="key">Table key to resolve.</param>
    /// <param name="value">The bytes of the string, which Luau owns, or <c>default</c> when the value is <c>nil</c>.</param>
    /// <param name="isNil">Set to <c>true</c> when the value is <c>nil</c>.</param>
    /// <returns><c>true</c> when the value is a string or <c>nil</c>; otherwise <c>false</c>.</returns>
    public static bool TryGetUtf8StringSpanOrNil(
        in LuauTable table,
        IntoLuau key,
        out ReadOnlySpan<byte> value,
        out bool isNil
    ) => table.TryGetUtf8StringOrNil(key, out value, out isNil, out _);

    /// <summary> Attempts to get the bytes of the buffer at <paramref name="key"/> without copying them. </summary>
    /// <param name="table">The table to read from.</param>
    /// <param name="key">Table key to resolve.</param>
    /// <param name="value">The bytes of the buffer, which Luau owns.</param>
    /// <returns><c>true</c> when the value exists and is a buffer; otherwise <c>false</c>.</returns>
    public static bool TryGetBufferSpan(in LuauTable table, IntoLuau key, out ReadOnlySpan<byte> value) =>
        table.TryGetBuffer(key, out value, out _);

    /// <summary> Attempts to get the bytes of the buffer or <c>nil</c> at <paramref name="key"/> without copying them. </summary>
    /// <param name="table">The table to read from.</param>
    /// <param name="key">Table key to resolve.</param>
    /// <param name="value">The bytes of the buffer, which Luau owns, or <c>default</c> when the value is <c>nil</c>.</param>
    /// <param name="isNil">Set to <c>true</c> when the value is <c>nil</c>.</param>
    /// <returns><c>true</c> when the value is a buffer or <c>nil</c>; otherwise <c>false</c>.</returns>
    public static bool TryGetBufferSpanOrNil(
        in LuauTable table,
        IntoLuau key,
        out ReadOnlySpan<byte> value,
        out bool isNil
    ) => table.TryGetBufferOrNil(key, out value, out isNil, out _);
}
