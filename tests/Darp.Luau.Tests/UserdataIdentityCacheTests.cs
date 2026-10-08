using Darp.Luau.Tests.Fixtures;
using Shouldly;

namespace Darp.Luau.Tests;

public sealed class UserdataIdentityCacheTests : IDisposable
{
    private readonly LuauState _state = new();

    [Fact]
    public void Cache_SameManagedInstance_ShouldReuseIdentity()
    {
        var value = new ValueUserdata { Value = 11 };

        using LuauUserdata first = _state.GetOrCreateUserdata(value);
        using LuauUserdata second = _state.GetOrCreateUserdata(value);

        _state.Globals.Set("first", first);
        _state.Globals.Set("second", second);
        _state.Load("sameIdentity = first == second").Execute();

        _state.Globals.TryGet("sameIdentity", out bool sameIdentity).ShouldBeTrue();
        sameIdentity.ShouldBeTrue();
    }

    [Fact]
    public void Cache_DifferentManagedInstances_ShouldNotReuseIdentity()
    {
        var firstValue = new ValueUserdata();
        var secondValue = new ValueUserdata();

        using LuauUserdata first = _state.GetOrCreateUserdata(firstValue);
        using LuauUserdata second = _state.GetOrCreateUserdata(secondValue);

        _state.Globals.Set("first", first);
        _state.Globals.Set("second", second);
        _state.Load("sameIdentity = first == second").Execute();

        _state.Globals.TryGet("sameIdentity", out bool sameIdentity).ShouldBeTrue();
        sameIdentity.ShouldBeFalse();
    }

    [Fact]
    public void Cache_DisposingOneWrapper_ShouldNotInvalidateOtherWrapper()
    {
        var value = new ValueUserdata { Value = 44 };

        using LuauUserdata first = _state.GetOrCreateUserdata(value);
        using LuauUserdata second = _state.GetOrCreateUserdata(value);

        first.Dispose();
        second.TryGetManaged(out ValueUserdata? resolved, out string? error).ShouldBeTrue(error);
        ReferenceEquals(value, resolved).ShouldBeTrue();
    }

    [Fact]
    public void Cache_StrongLuaReference_ShouldKeepIdentityReusable()
    {
        var value = new ValueUserdata();

        using LuauUserdata keepAlive = _state.GetOrCreateUserdata(value);
        _state.Globals.Set("keepAlive", keepAlive);

        keepAlive.Dispose();

        using LuauUserdata recreated = _state.GetOrCreateUserdata(value);
        _state.Globals.Set("recreated", recreated);
        _state.Load("sameIdentity = keepAlive == recreated").Execute();

        _state.Globals.TryGet("sameIdentity", out bool sameIdentity).ShouldBeTrue();
        sameIdentity.ShouldBeTrue();
    }

    [Fact]
    public void Cache_WhenUserdataCollected_ShouldCreateNewIdentity()
    {
        var value = new ValueUserdata();

        LuauUserdata oldUserdata = _state.GetOrCreateUserdata(value);
        _state.Globals.Set("oldUserdata", oldUserdata);
        oldUserdata.Dispose();

        _state
            .Load(
                """
                weakKeys = setmetatable({}, { __mode = "k" })
                weakKeys[oldUserdata] = true
                oldUserdata = nil
                """
            )
            .Execute();
        _state.CollectGarbage();

        _state.Load("return next(weakKeys) == nil").Execute<bool>().ShouldBeTrue();

        using LuauUserdata newUserdata = _state.GetOrCreateUserdata(value);
        _state.Globals.Set("newUserdata", newUserdata);
        _state.Load("reusedAfterCollect = weakKeys[newUserdata] == true").Execute();

        _state.Globals.TryGet("reusedAfterCollect", out bool reusedAfterCollect).ShouldBeTrue();
        reusedAfterCollect.ShouldBeFalse();
    }

    [Fact]
    public void Cache_ShouldStayStableUnderRepeatedLookups()
    {
        var value = new ValueUserdata();

        using LuauUserdata baseline = _state.GetOrCreateUserdata(value);
        _state.Globals.Set("baseline", baseline);

        for (int i = 0; i < 1000; i++)
        {
            using LuauUserdata current = _state.GetOrCreateUserdata(value);
            _state.Globals.Set("current", current);
            _state.Load("sameIdentity = baseline == current").Execute();
            _state.Globals.TryGet("sameIdentity", out bool sameIdentity).ShouldBeTrue();
            sameIdentity.ShouldBeTrue();
        }
    }

    public void Dispose()
    {
        _state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(1UL);
        _state.Dispose();
    }
}
