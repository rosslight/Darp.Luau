using Shouldly;

namespace Darp.Luau.Tests.Table;

/// <summary> Enumerating a table: its pairs and its list part, through an owned reference and through a view. </summary>
public sealed class LuauTableEnumerationTests : IDisposable
{
    private readonly LuauState _state = new();

    private ulong ActiveReferences => _state.MemoryStatistics.ActiveRegistryReferences;

    private LuauTable Table(string expression) => _state.Load($"return {expression}").Execute<LuauTable>();

    [Fact]
    public void TableArgument_ShouldBeEnumeratedWithoutAnOwnedReference()
    {
        using LuauFunction function = _state.CreateFunctionManual(static args =>
        {
            LuauTableView table = args[1].Get<LuauTableView>();
            ulong references = args.State.MemoryStatistics.ActiveRegistryReferences;
            double sum = 0;
            int count = 0;
            foreach (KeyValuePair<LuauValue, LuauValue> pair in table)
            {
                // A number holds no reference, so there is nothing to dispose.
                sum += pair.Key.Get<double>() * pair.Value.Get<double>();
                count++;
            }

            bool tookNoReference = args.State.MemoryStatistics.ActiveRegistryReferences == references;
            return LuauReturn.Ok(sum, count, tookNoReference);
        });
        using LuauTable table = Table("{ [1] = 10, [2] = 20, [10] = 5 }");

        function.Invoke<double, int, bool>(table).ShouldBe((100, 3, true));
    }

    [Fact]
    public void TableArgument_ShouldGiveEveryPairWithAReferenceOfItsOwn()
    {
        using LuauFunction function = _state.CreateFunctionManual(static args =>
        {
            LuauTableView table = args[1].Get<LuauTableView>();
            var names = new List<string>();
            foreach (KeyValuePair<LuauValue, LuauValue> pair in table)
            {
                using LuauValue key = pair.Key;
                using LuauValue value = pair.Value;
                using LuauTable nested = value.Get<LuauTable>();
                names.Add($"{key.Get<string>()}={nested.Get<int>("id")}");
            }

            names.Sort(StringComparer.Ordinal);
            return LuauReturn.Ok(string.Join(' ', names));
        });
        using LuauTable table = Table("{ a = { id = 1 }, b = { id = 2 } }");
        ulong baseline = ActiveReferences;

        function.Invoke<string>(table).ShouldBe("a=1 b=2");
        ActiveReferences.ShouldBe(baseline);
    }

    [Fact]
    public void TableArgument_IPairs_ShouldStopAtTheFirstValueThatIsNotOfTheType()
    {
        using LuauFunction function = _state.CreateFunctionManual(static args =>
        {
            double sum = 0;
            int last = 0;
            foreach ((int index, double value) in args[1].Get<LuauTableView>().IPairs<double>())
            {
                sum += value;
                last = index;
            }

            return LuauReturn.Ok(sum, last);
        });

        using LuauTable numbers = Table("{ 1, 2, 3 }");
        function.Invoke<double, int>(numbers).ShouldBe((6, 3));
        using LuauTable mixed = Table("{ 1, 2, 'three', 4 }");
        function.Invoke<double, int>(mixed).ShouldBe((3, 2));
        using LuauTable holed = Table("{ 1, nil, 3 }");
        function.Invoke<double, int>(holed).ShouldBe((1, 1));
        using LuauTable empty = Table("{ name = 'no list part' }");
        function.Invoke<double, int>(empty).ShouldBe((0, 0));
    }

    [Fact]
    public void TableArgument_IPairs_ShouldStopAtNilAlsoForATypeThatReadsNil()
    {
        using LuauFunction function = _state.CreateFunctionManual(static args =>
        {
            LuauTableView table = args[1].Get<LuauTableView>();
            int values = 0;
            foreach ((int _, LuauValue value) in table.IPairs())
            {
                using (value)
                    values++;
            }

            int nullable = 0;
            foreach ((int _, int? _) in table.IPairs<int?>())
                nullable++;
            return LuauReturn.Ok(values, nullable);
        });
        using LuauTable table = Table("{ 1, 'two', nil, 4 }");

        // 'two' is a value, but it is not an int.
        function.Invoke<int, int>(table).ShouldBe((2, 1));
    }

    [Fact]
    public void TableArgument_IPairs_ShouldGiveOwnedReferencesThatTheCallerDisposes()
    {
        using LuauFunction function = _state.CreateFunctionManual(static args =>
        {
            int sum = 0;
            foreach ((int _, LuauTable item) in args[1].Get<LuauTableView>().IPairs<LuauTable>())
            {
                using (item)
                    sum += item.Get<int>("id");
            }

            return LuauReturn.Ok(sum);
        });
        using LuauTable table = Table("{ { id = 1 }, { id = 2 }, { id = 4 } }");
        ulong baseline = ActiveReferences;

        function.Invoke<int>(table).ShouldBe(7);
        ActiveReferences.ShouldBe(baseline);
    }

    [Fact]
    public void TableArgument_OnACoroutine_ShouldBeEnumerated()
    {
        using LuauFunction function = _state.CreateFunctionManual(static args =>
        {
            LuauTableView table = args[1].Get<LuauTableView>();
            double sum = 0;
            foreach ((int _, double value) in table.IPairs<double>())
                sum += value;
            foreach (KeyValuePair<LuauValue, LuauValue> pair in table)
                sum += pair.Value.Get<double>();
            return LuauReturn.Ok(sum);
        });
        _state.Globals.Set("f", function);

        _state
            .Load("local co = coroutine.wrap(function() return f({ 1, 2, 3 }) end) return co()")
            .Execute<double>()
            .ShouldBe(12);
    }

    [Fact]
    public void OwnedTable_IPairs_ShouldStillBeASequence()
    {
        using LuauTable table = Table("{ 'a', 'b', 3 }");

        table.IPairs<string>().Select(pair => pair.Value).ShouldBe(["a", "b"]);
        table.IPairs<string>().Select(pair => pair.Key).ShouldBe([1, 2]);
    }

    [Fact]
    public void Pairs_WithAVector_ShouldThrowAndLeaveNothingBehind()
    {
        // A vector has no managed form. In the first table the vector is read after the table next to it.
        using LuauTable vectorKey = Table("{ [vector.create(1, 2, 3)] = {} }");
        using LuauTable vectorValue = Table("{ [{}] = vector.create(1, 2, 3) }");
        using LuauTable other = Table("{ marker = 7 }");
        ulong baseline = ActiveReferences;

        Should.Throw<NotSupportedException>(() =>
        {
            foreach (KeyValuePair<LuauValue, LuauValue> _ in vectorKey) { }
        });
        Should.Throw<NotSupportedException>(() =>
        {
            foreach (KeyValuePair<LuauValue, LuauValue> _ in vectorValue) { }
        });

        ActiveReferences.ShouldBe(baseline);
        // The stack is as it was: the next read finds its value.
        other.Get<int>("marker").ShouldBe(7);
    }

    [Fact]
    public void IPairs_WithAVector_ShouldThrowForAValueAndStopForAnotherType()
    {
        using LuauTable table = Table("{ 1, vector.create(1, 2, 3), 3 }");
        using LuauTable other = Table("{ marker = 7 }");
        ulong baseline = ActiveReferences;

        Should.Throw<NotSupportedException>(() => table.IPairs().ToArray());
        table.IPairs<double>().Select(pair => pair.Value).ShouldBe([1]);

        ActiveReferences.ShouldBe(baseline);
        other.Get<int>("marker").ShouldBe(7);
    }

    public void Dispose() => _state.Dispose();
}
