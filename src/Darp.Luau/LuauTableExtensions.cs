using System.Diagnostics.CodeAnalysis;

namespace Darp.Luau;

/// <summary> Table extensions </summary>
public static class LuauTableExtensions
{
    /// <summary> Tries to get a value of a specific type from the table </summary>
    /// <param name="table"> The table to get the value from </param>
    /// <param name="key"> The key to look up </param>
    /// <param name="value"> The value, if present and of the correct type </param>
    /// <typeparam name="TValue"> The type of the value </typeparam>
    /// <returns> True, if the value could be retrieved and has the correct type. False, otherwise </returns>
    /// <exception cref="NotSupportedException">
    /// Thrown when <typeparamref name="TValue"/> is a span. A span would point into memory that only the table
    /// keeps alive. Read a copy, or use <see cref="LuauMarshal"/>.
    /// </exception>
    public static bool TryGet<TValue>(this in LuauTable table, IntoLuau key, [NotNullWhen(true)] out TValue? value)
        where TValue : allows ref struct
    {
        if (typeof(TValue) == typeof(ReadOnlySpan<byte>) || typeof(TValue) == typeof(ReadOnlySpan<char>))
        {
            throw new NotSupportedException(
                $"A table value cannot be read as a span: only the table keeps its memory alive. "
                    + $"Read a copy, or use {nameof(LuauMarshal)}."
            );
        }

        value = default;
        if (!table.TryGetLuauValue(key, out LuauValue luauValue))
            return false;

        using (luauValue)
        {
            return luauValue.TryGet(out value, acceptNil: false);
        }
    }

    /// <summary> Iterates on a table until the first value not matching the given type </summary>
    /// <param name="table"> The table to enumerate </param>
    /// <typeparam name="T"> The type to interpret the values with </typeparam>
    /// <returns> An enumerable with index,value pairs </returns>
    /// <remarks> Starts at index 1 and goes as long as there is no nil/different type value </remarks>
    public static IEnumerable<KeyValuePair<int, T>> IPairs<T>(this LuauTable table)
    {
        foreach ((int i, LuauValue value) in table.IPairs())
        {
            using (value)
            {
                if (value.TryGet(out T? v))
                    yield return new KeyValuePair<int, T>(i, v);
                else
                    break;
            }
        }
    }

    /// <summary>
    /// Reinterprets the <see cref="LuauValue"/> as <typeparamref name="T"/> or returns null if different type
    /// </summary>
    /// <param name="value"> The value to cast </param>
    /// <typeparam name="T"> The type to cast to </typeparam>
    /// <returns> The value as the requested type or null </returns>
    public static T? As<T>(this in LuauValue value)
        where T : struct => value.Type is not LuauValueType.Nil && value.TryGet(out T v) ? v : null;
}

/// <summary> Additional table extensions. Necessary to have constraints both on class and struct types </summary>
public static class LuauTableClassExtensions
{
    /// <summary>
    /// Reinterprets the <see cref="LuauValue"/> as <typeparamref name="T"/> or returns null if different type
    /// </summary>
    /// <param name="value"> The value to cast </param>
    /// <typeparam name="T"> The type to cast to </typeparam>
    /// <returns> The value as the requested type or null </returns>
    public static T? As<T>(this in LuauValue value)
        where T : class => value.Type is not LuauValueType.Nil && value.TryGet(out T? v) ? v : null;
}
