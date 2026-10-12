using Shouldly;

namespace Darp.Luau.Tests.Table;

public sealed class LuauTableListTests : IDisposable
{
    private readonly LuauState _lua = new();

    [Fact]
    public void CreateTable_span_values_creates_1_based_array()
    {
        using LuauTable table = _lua.CreateTable([1, 2, 3]);

        table[1].TryGet(out double v1).ShouldBeTrue();
        table[2].TryGet(out double v2).ShouldBeTrue();
        table[3].TryGet(out double v3).ShouldBeTrue();

        v1.ShouldBe(1);
        v2.ShouldBe(2);
        v3.ShouldBe(3);
    }

    [Fact]
    public void ListCount_matches_dense_array_length()
    {
        using LuauTable table = _lua.CreateTable([1, 2, 3]);

        table.ListCount.ShouldBe(3);
    }

    [Fact]
    public void IPairs_Count_matches_ListCount()
    {
        using LuauTable table = _lua.CreateTable([1, 2, 3]);

        table.ListCount.ShouldBe(3);
        table.IPairs().Count.ShouldBe(table.ListCount);
    }

    [Fact]
    public void IPairs_enumerates_dense_array_in_order()
    {
        using LuauTable table = _lua.CreateTable([1, 4, 9]);

        KeyValuePair<int, LuauValue>[] content = table.IPairs().ToArray();
        content[0].Key.ShouldBe(1);
        content[0].Value.Get<double>().ShouldBe(1);
        content[1].Key.ShouldBe(2);
        content[1].Value.Get<double>().ShouldBe(4);
        content[2].Key.ShouldBe(3);
        content[2].Value.Get<double>().ShouldBe(9);
    }

    [Fact]
    public void Table_enumerator_enumerates_all_dense_array_keys_without_order_guarantee()
    {
        using LuauTable table = _lua.CreateTable([1, 4, 9]);

        KeyValuePair<LuauValue, LuauValue>[] content = table.ToArray();
        content[0].Key.Get<double>().ShouldBe(1);
        content[0].Value.Get<double>().ShouldBe(1);
        content[1].Key.Get<double>().ShouldBe(2);
        content[1].Value.Get<double>().ShouldBe(4);
        content[2].Key.Get<double>().ShouldBe(3);
        content[2].Value.Get<double>().ShouldBe(9);
    }

    [Fact]
    public void Table_enumerator_enumerates_non_integer_keys()
    {
        using LuauTable table = _lua.CreateTable();
        table.Set("a", 1);
        table.Set(2, 3);

        KeyValuePair<LuauValue, LuauValue>[] content = table.ToArray();
        try
        {
            content[0].Key.Get<string>().ShouldBe("a");
            content[0].Value.Get<double>().ShouldBe(1);
            content[1].Key.Get<double>().ShouldBe(2);
            content[1].Value.Get<double>().ShouldBe(3);
        }
        finally
        {
            foreach (KeyValuePair<LuauValue, LuauValue> item in content)
            {
                item.Key.Dispose();
                item.Value.Dispose();
            }
        }
    }

    [Fact]
    public void IPairs_T_typed_enumerates_dense_array_in_order()
    {
        using LuauTable table = _lua.CreateTable([1, 4, 9]);

        KeyValuePair<int, double>[] content = table.IPairs<double>().ToArray();
        content.Length.ShouldBe(3);
        content[0].Key.ShouldBe(1);
        content[0].Value.ShouldBe(1);
        content[1].Key.ShouldBe(2);
        content[1].Value.ShouldBe(4);
        content[2].Key.ShouldBe(3);
        content[2].Value.ShouldBe(9);
    }

    [Fact]
    public void IPairs_T_typed_enumerates_until_first_type_mismatch()
    {
        using LuauTable table = _lua.CreateTable();
        table.Set(1, 1);
        table.Set(2, "not a number");
        table.Set(3, 3);

        KeyValuePair<int, double>[] content = table.IPairs<double>().ToArray();
        content.Length.ShouldBe(1);
        content[0].Key.ShouldBe(1);
        content[0].Value.ShouldBe(1);
    }

    public void Dispose()
    {
        _lua.MemoryStatistics.ActiveRegistryReferences.ShouldBe(1UL);
        _lua.Dispose();
    }
}
