using Darp.Luau.Tests.Fixtures;
using Shouldly;

namespace Darp.Luau.Tests.Table;

/// <summary> Table access from the host runs metamethods, and a metamethod that fails must not take the process down. </summary>
public sealed class LuauTableMetamethodTests : IDisposable
{
    private readonly LuauState _state = new();

    private LuauTable Create(string source) => _state.Load(source).Execute<LuauTable>();

    [Fact]
    public void Get_ShouldUseIndexMetamethod()
    {
        using LuauTable table = Create(
            """
            return setmetatable({ own = 1 }, { __index = function(_, key) return #key end })
            """
        );

        table.GetNumber("own").ShouldBe(1);
        table.GetNumber("four").ShouldBe(4);
        table.ContainsKey("anything").ShouldBeTrue();
    }

    [Fact]
    public void Get_ShouldUseIndexTable()
    {
        using LuauTable table = Create("return setmetatable({}, { __index = { inherited = 7 } })");

        table.GetNumber("inherited").ShouldBe(7);
        table.ContainsKey("missing").ShouldBeFalse();
    }

    [Fact]
    public void Get_WithMetatableWithoutIndex_ShouldReadNil()
    {
        using LuauTable table = Create("return setmetatable({}, { __tostring = function() return 'x' end })");

        table.TryGet("missing", out LuauValue value).ShouldBeTrue();
        value.Type.ShouldBe(LuauValueType.Nil);
        table.ContainsKey("missing").ShouldBeFalse();
    }

    [Fact]
    public void Get_WhenIndexMetamethodFails_ShouldReportTheError()
    {
        using LuauTable table = Create(
            """
            return setmetatable({}, { __index = function() error("boom from __index") end })
            """
        );

        table.TryGetNumber("missing", out double _).ShouldBeFalse();
        table.TryGet("missing", out LuauValue _).ShouldBeFalse();
        Should.Throw<LuaGetException>(() => table.GetNumber("missing")).Message.ShouldContain("boom from __index");
        Should.Throw<LuaException>(() => table.ContainsKey("missing")).Message.ShouldContain("boom from __index");

        // The failed reads left the stack balanced and the state usable.
        _state.Load("return 1 + 1").Execute<int>().ShouldBe(2);
    }

    [Fact]
    public void Get_WhenIndexMetamethodYields_ShouldReportTheError()
    {
        using LuauTable table = Create("return setmetatable({}, { __index = function() coroutine.yield() end })");

        table.TryGetNumber("missing", out double _).ShouldBeFalse();
    }

    [Fact]
    public void Set_ShouldUseNewIndexMetamethod()
    {
        using LuauTable table = Create(
            """
            log = {}
            return setmetatable({ own = 1 }, { __newindex = function(t, key, value) log[key] = value * 2 end })
            """
        );

        table.Set("own", 5);
        table.Set("fresh", 21);

        table.GetNumber("own").ShouldBe(5);
        table.ContainsKey("fresh").ShouldBeFalse();
        using LuauTable log = _state.Globals.GetLuauTable("log");
        log.GetNumber("fresh").ShouldBe(42);
    }

    [Fact]
    public void Set_WhenNewIndexMetamethodFails_ShouldThrow()
    {
        using LuauTable table = Create(
            """
            return setmetatable({}, { __newindex = function() error("boom from __newindex") end })
            """
        );

        Should.Throw<LuaException>(() => table.Set("key", 1)).Message.ShouldContain("boom from __newindex");

        _state.Load("return 1 + 1").Execute<int>().ShouldBe(2);
    }

    [Fact]
    public void Set_OnFrozenTable_ShouldThrow()
    {
        using LuauTable table = Create("return table.freeze({ value = 1 })");

        Should.Throw<LuaException>(() => table.Set("value", 2)).Message.ShouldContain("readonly");

        table.GetNumber("value").ShouldBe(1);
    }

    [Fact]
    public void Set_OnFrozenTableWithNewIndex_ShouldWriteLikeAScript()
    {
        using LuauTable proxy = Create(
            """
            backing = {}
            return table.freeze(setmetatable({ own = 1 }, { __newindex = backing }))
            """
        );

        // __newindex takes a new key even though the table is frozen. An existing key is a write to the table.
        proxy.Set("fresh", 23);
        Should.Throw<LuaException>(() => proxy.Set("own", 2)).Message.ShouldContain("readonly");

        using LuauTable backing = _state.Globals.GetLuauTable("backing");
        backing.GetNumber("fresh").ShouldBe(23);
        proxy.GetNumber("own").ShouldBe(1);
    }

    [Fact]
    public void Get_WithAUserdataFactoryKey_ShouldCreateTheKeyOnce()
    {
        using LuauTable table = Create("return setmetatable({}, { __index = function() return 'from __index' end })");
        int created = 0;
        IntoLuau key = IntoLuau.FromUserdata(state =>
        {
            created++;
            return state.GetOrCreateUserdata(new ValueUserdata());
        });

        table.GetUtf8String(key).ShouldBe("from __index");

        created.ShouldBe(1);
    }

    [Fact]
    public void Set_WithAKeyLuauRejects_ShouldThrow()
    {
        using LuauTable table = _state.CreateTable();

        Should.Throw<LuaException>(() => table.Set(double.NaN, 1)).Message.ShouldContain("NaN");
        Should.Throw<LuaException>(() => table.Set((LuauValue)double.NaN, 1)).Message.ShouldContain("NaN");
        // A nil that only shows when the key is pushed: Luau reports it like any other failed write.
        Should.Throw<LuaException>(() => table.Set(default(LuauValue), 1)).Message.ShouldContain("nil");

        table.ContainsKey(double.NaN).ShouldBeFalse();
        table.ContainsKey(default(LuauValue)).ShouldBeFalse();
        _state.Load("return 1 + 1").Execute<int>().ShouldBe(2);
    }

    [Fact]
    public void Set_WhenTheValueCannotBePushed_ShouldLeaveTheStackBalanced()
    {
        using LuauTable table = _state.CreateTable();
        LuauTable disposed = _state.CreateTable();
        disposed.Dispose();

        Should.Throw<Exception>(() => table.Set("key", disposed));
        Should.Throw<Exception>(() => table.ContainsKey(disposed));

        table.Set("key", 3);
        table.GetNumber("key").ShouldBe(3);
    }

    [Fact]
    public void ToString_ShouldNotRunScriptCode()
    {
        using LuauTable table = Create(
            """
            tostringCalls = 0
            tostring = function() tostringCalls += 1 error("replaced tostring") end
            return setmetatable({}, { __tostring = function() tostringCalls += 1 error("boom from __tostring") end })
            """
        );
        using LuauFunction function = _state.Load("return function() end").Execute<LuauFunction>();

        table.ToString().ShouldStartWith("table: 0x");
        function.ToString().ShouldStartWith("function: 0x");
        _state.Globals.GetNumber("tostringCalls").ShouldBe(0);
    }

    [Fact]
    public void ToString_OfADisposedReference_ShouldNotThrow()
    {
        LuauTable table = _state.CreateTable();
        table.Dispose();

        table.ToString().ShouldBe("<disposed>");
    }

    public void Dispose() => _state.Dispose();
}
