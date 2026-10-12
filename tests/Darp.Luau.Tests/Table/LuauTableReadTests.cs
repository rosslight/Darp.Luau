using Darp.Luau.Tests.Fixtures;
using Shouldly;

namespace Darp.Luau.Tests.Table;

/// <summary> Reading the values of a table. Which type reads which value: <see cref="TypedReadTests"/>. </summary>
public sealed class LuauTableReadTests : IDisposable
{
    private readonly LuauState _state = new();

    private ulong ActiveReferences => _state.MemoryStatistics.ActiveRegistryReferences;

    private LuauTable Table(string expression) => _state.Load($"return {expression}").Execute<LuauTable>();

    [Fact]
    public void Get_ShouldReadTheValueAsTheRequestedType()
    {
        using LuauTable table = Table("{ number = 42, text = 'héllo', flag = true, bytes = buffer.fromstring('abc') }");

        table.Get<int>("number").ShouldBe(42);
        table.Get<double>("number").ShouldBe(42);
        table.Get<string>("text").ShouldBe("héllo");
        table.Get<bool>("flag").ShouldBeTrue();
        table.Get<byte[]>("bytes").ShouldBe("abc"u8.ToArray());
    }

    [Fact]
    public void Get_ShouldReadWithAKeyOfEveryType()
    {
        using LuauTable table = Table("{ [1] = 'one', [true] = 'yes', name = 'text' }");

        table.Get<string>(1).ShouldBe("one");
        table.Get<string>(true).ShouldBe("yes");
        table.Get<string>("name").ShouldBe("text");
    }

    [Fact]
    public void Get_WithAnotherType_ShouldSayWhatWasExpectedAndFound()
    {
        using LuauTable table = Table("{ text = 'text' }");

        Should
            .Throw<LuaGetException>(() => table.Get<int>("text"))
            .Message.ShouldBe("table value: number expected, got string");
        Should
            .Throw<LuaGetException>(() => table.Get<int>("missing"))
            .Message.ShouldBe("table value: number expected, got nil");
        Should
            .Throw<LuaGetException>(() => table.Get<string>("missing"))
            .Message.ShouldBe("table value: string expected, got nil");
    }

    [Fact]
    public void Get_ShouldReadAMissingKeyAsNilForATypeThatHasAValueForIt()
    {
        using LuauTable table = Table("{}");

        table.Get<int?>("missing").ShouldBeNull();
        table.Get<LuauTable?>("missing").ShouldBeNull();
        table.Get<LuauValue>("missing").Type.ShouldBe(LuauValueType.Nil);
        table["missing"].Type.ShouldBe(LuauValueType.Nil);
    }

    [Fact]
    public void GetOrNull_ShouldReadAMissingKeyAsNull()
    {
        using LuauTable table = Table("{ text = 'text', number = 1 }");

        table.GetOrNull<string>("missing").ShouldBeNull();
        table.GetOrNull<string>("text").ShouldBe("text");
        Should
            .Throw<LuaGetException>(() => table.GetOrNull<string>("number"))
            .Message.ShouldBe("table value: string expected, got number");
    }

    [Fact]
    public void TryGet_ShouldTellWhetherTheValueIsOfTheType()
    {
        using LuauTable table = Table("{ number = 42, text = 'text' }");

        table.TryGet("number", out int number).ShouldBeTrue();
        number.ShouldBe(42);
        table.TryGet("text", out string? text).ShouldBeTrue();
        text.ShouldBe("text");
        table.TryGet("text", out int _).ShouldBeFalse();
        table.TryGet("missing", out int _).ShouldBeFalse();
        table.TryGet("missing", out string? missing).ShouldBeFalse();
        missing.ShouldBeNull();
    }

    [Fact]
    public void TryGet_ShouldReadAMissingKeyAsTheNullOfANullableValueType()
    {
        using LuauTable table = Table("{ number = 42, text = 'text' }");

        table.TryGet("missing", out int? missing).ShouldBeTrue();
        missing.ShouldBeNull();
        table.TryGet("number", out int? number).ShouldBeTrue();
        number.ShouldBe(42);
        table.TryGet("text", out int? _).ShouldBeFalse();
    }

    [Fact]
    public void TryGetOrNull_ShouldTellAMissingKeyFromAnotherType()
    {
        using LuauTable table = Table("{ number = 42, text = 'text' }");

        table.TryGetOrNull("missing", out string? missing).ShouldBeTrue();
        missing.ShouldBeNull();
        table.TryGetOrNull("text", out string? text).ShouldBeTrue();
        text.ShouldBe("text");
        table.TryGetOrNull("number", out string? _).ShouldBeFalse();
    }

    [Fact]
    public void Get_ShouldReadAnOwnedReferenceThatTheCallerDisposes()
    {
        using LuauTable table = Table("{ nested = { answer = 42 }, add = function(a, b) return a + b end }");
        ulong baseline = ActiveReferences;

        using (LuauTable nested = table.Get<LuauTable>("nested"))
        using (LuauFunction add = table.Get<LuauFunction>("add"))
        {
            ActiveReferences.ShouldBe(baseline + 2);
            nested.Get<int>("answer").ShouldBe(42);
            add.Invoke<int>(40, 2).ShouldBe(42);
        }

        ActiveReferences.ShouldBe(baseline);
    }

    [Fact]
    public void Get_ShouldReadManagedUserdataAsTheObject()
    {
        var userdata = new ValueUserdata { Value = 7 };
        using LuauTable table = _state.CreateTable();
        table.Set("value", userdata);

        table.Get<ValueUserdata>("value").ShouldBeSameAs(userdata);
        table.TryGet("value", out OtherValueUserdata? _).ShouldBeFalse();
        table.GetOrNull<ValueUserdata>("missing").ShouldBeNull();
    }

    [Fact]
    public void Read_WhenIndexRaisesAnError_ShouldReportTheErrorOfTheScript()
    {
        using LuauTable table = Table("setmetatable({}, { __index = function() error('no such key', 0) end })");

        Should.Throw<LuaGetException>(() => table.Get<int>("key")).Message.ShouldBe("no such key");
        Should.Throw<LuaGetException>(() => table.GetOrNull<string>("key")).Message.ShouldBe("no such key");
        table.TryGet("key", out int _).ShouldBeFalse();
        table.TryGetOrNull("key", out string? _).ShouldBeFalse();
        table["key"].Type.ShouldBe(LuauValueType.Nil);
    }

    [Fact]
    public void Read_ShouldUseTheIndexMetamethod()
    {
        using LuauTable table = Table("setmetatable({}, { __index = function(_, key) return key .. '!' end })");

        table.Get<string>("hello").ShouldBe("hello!");
        table.ContainsKey("anything").ShouldBeTrue();
    }

    [Fact]
    public void Read_ShouldLeaveTheStackAndTheReferencesAsItFoundThem()
    {
        using LuauTable table = Table("{ text = 'text', vector = vector.create(1, 2, 3), nested = {} }");
        int top = _state.GetStackTop();
        ulong references = ActiveReferences;

        table.TryGet("text", out int _).ShouldBeFalse();
        Should.Throw<LuaGetException>(() => table.Get<int>("text"));
        Should.Throw<NotSupportedException>(() => table.Get<DateTime>("text"));
        Should.Throw<NotSupportedException>(() => table.TryGet("text", out DateTime _));
        // A vector has no managed form.
        Should.Throw<NotSupportedException>(() => table.Get<LuauValue>("vector"));
        Should.Throw<NotSupportedException>(() => table["vector"]);
        table.Get<LuauTable>("nested").Dispose();
        table.GetOrNull<string>("missing").ShouldBeNull();

        _state.GetStackTop().ShouldBe(top);
        ActiveReferences.ShouldBe(references);
    }

    [Fact]
    public void TryGet_ThatFails_ShouldNotAllocate()
    {
        using LuauTable table = Table("{ text = 'text' }");
        using LuauTable failing = Table("setmetatable({}, { __index = function() error('no such key', 0) end })");
        for (int i = 0; i < 100; i++)
            Fail(table, failing);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            Fail(table, failing);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.ShouldBe(0);

        static void Fail(LuauTable table, LuauTable failing)
        {
            table.TryGet("missing", out int _).ShouldBeFalse();
            table.TryGet("text", out int _).ShouldBeFalse();
            // The error of the script is not turned into text that nobody reads.
            failing.TryGet("key", out int _).ShouldBeFalse();
            failing.TryGetOrNull("key", out string? _).ShouldBeFalse();
        }
    }

    [Fact]
    public void DisposedTable_ShouldThrowFromEveryRead()
    {
        LuauTable table = Table("{ number = 42 }");
        table.Dispose();

        Should.Throw<ObjectDisposedException>(() => table.Get<int>("number"));
        Should.Throw<ObjectDisposedException>(() => table.GetOrNull<string>("number"));
        Should.Throw<ObjectDisposedException>(() => table.TryGet("number", out int _));
        Should.Throw<ObjectDisposedException>(() => table.TryGetOrNull("number", out string? _));
        Should.Throw<ObjectDisposedException>(() => table["number"]);
        Should.Throw<ObjectDisposedException>(() => table.ContainsKey("number"));
        Should.Throw<ObjectDisposedException>(() => table.Set("number", 1));
        Should.Throw<ObjectDisposedException>(() => table.ListCount);
    }

    [Fact]
    public void TableArgument_ShouldBeReadAndWrittenWithoutAnOwnedReference()
    {
        using LuauFunction function = _state.CreateFunctionManual(static args =>
        {
            LuauTableView table = args[1].Get<LuauTableView>();
            int sum = table.Get<int>("a") + table.Get<int?>("b").GetValueOrDefault(100);
            table.Set("sum", sum);
            table.TryGet("name", out string? name);
            using LuauTable nested = table.Get<LuauTable>("nested");
            return LuauReturn.Ok(
                $"{sum} {name} {table.GetOrNull<string>("missing") ?? "nil"} {table.ContainsKey("a")} "
                    + $"{table.ListCount} {nested.Get<int>("x")} {table["a"].Get<int>()}"
            );
        });
        _state.Globals.Set("f", function);
        ulong baseline = ActiveReferences;

        string? result = _state
            .Load("local t = { 10, 20, a = 1, name = 'n', nested = { x = 7 } } return f(t) .. ' ' .. t.sum")
            .Execute<string>();

        result.ShouldBe("101 n nil True 2 7 1 101");
        ActiveReferences.ShouldBe(baseline);
    }

    [Fact]
    public void TableArgument_OnACoroutine_ShouldBeReadAndWritten()
    {
        using LuauFunction function = _state.CreateFunctionManual(static args =>
        {
            LuauTableView table = args[1].Get<LuauTableView>();
            table.Set("seen", table.Get<int>("value") + 1);
            return LuauReturn.Ok(table.Get<int>("seen"));
        });
        _state.Globals.Set("f", function);

        _state
            .Load("local co = coroutine.wrap(function() local t = { value = 41 } return f(t) + t.seen end) return co()")
            .Execute<int>()
            .ShouldBe(84);
    }

    public void Dispose() => _state.Dispose();
}
