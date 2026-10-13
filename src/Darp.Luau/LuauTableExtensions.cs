namespace Darp.Luau;

/// <summary> Table extensions </summary>
public static class LuauTableExtensions
{
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
                if (value.TryGet<T>(out var v))
                    yield return new KeyValuePair<int, T>(i, v);
                else
                    break;
            }
        }
    }
}
