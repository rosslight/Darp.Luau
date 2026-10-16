using Shouldly;

namespace Darp.Luau.Tests;

public sealed class UserdataScopeTests : IDisposable
{
    private readonly LuauState _state = new();
    private readonly LuauFunction _keep;

    public UserdataScopeTests()
    {
        using LuauTable point = _state.GetTypeTable<Point>();
        _state.Globals.Set("Point", point);
        // Keeps a new point when asked to, and reads the kept one
        _keep = _state
            .Load(
                """
                local kept
                return function(create)
                    if create then
                        kept = Point.new(1, 2)
                    end
                    return kept.x
                end
                """
            )
            .Execute<LuauFunction>();
    }

    [Fact]
    public void Userdata_CreatedInAScope_IsReleasedWhenTheScopeEnds()
    {
        using (_state.BeginUserdataScope())
            _keep.Invoke<double>(true).ShouldBe(1);

        Should.Throw<LuaException>(() => _keep.Invoke<double>(false)).Message.ShouldContain("was released");
    }

    [Fact]
    public void Userdata_CreatedBeforeAScope_IsKept()
    {
        _keep.Invoke<double>(true);

        using (_state.BeginUserdataScope())
            _keep.Invoke<double>(false).ShouldBe(1);

        _keep.Invoke<double>(false).ShouldBe(1);
    }

    [Fact]
    public void Userdata_ReleasedByAScope_DoesNotSeeTheObjectThatGetsItsSlot()
    {
        using (_state.BeginUserdataScope())
            _keep.Invoke<double>(true);

        // The new point takes the slot of the released one
        using LuauUserdata other = _state.GetOrCreateUserdata(new Point(5, 6));

        Should.Throw<LuaException>(() => _keep.Invoke<double>(false)).Message.ShouldContain("was released");
    }

    [Fact]
    public void ManagedObject_PushedAgainAfterItsScope_GetsAWorkingUserdata()
    {
        var point = new Point(3, 4);
        using LuauFunction x = _state.Load("return function(point) return point.x end").Execute<LuauFunction>();

        using (_state.BeginUserdataScope())
            x.Invoke<double>(IntoLuau.FromUserdata(point)).ShouldBe(3);

        x.Invoke<double>(IntoLuau.FromUserdata(point)).ShouldBe(3);
    }

    [Fact]
    public void Scope_DisposedBeforeAScopeInsideIt_Throws_AndBothCanStillEnd()
    {
        UserdataScope outer = _state.BeginUserdataScope();
        UserdataScope inner = _state.BeginUserdataScope();
        _keep.Invoke<double>(true);

        DisposeError(outer).ShouldNotBeNull();
        _keep.Invoke<double>(false).ShouldBe(1);

        inner.Dispose();
        outer.Dispose();
        Should.Throw<LuaException>(() => _keep.Invoke<double>(false)).Message.ShouldContain("was released");
    }

    [Fact]
    public void Scope_DisposedTwice_Throws()
    {
        UserdataScope scope = _state.BeginUserdataScope();
        scope.Dispose();

        DisposeError(scope).ShouldNotBeNull();
    }

    [Fact]
    public void Scope_DisposedAgainAfterANewScopeBegan_Throws_AndLeavesTheNewScopeOpen()
    {
        UserdataScope old = _state.BeginUserdataScope();
        old.Dispose();
        UserdataScope current = _state.BeginUserdataScope();
        _keep.Invoke<double>(true);

        DisposeError(old).ShouldNotBeNull();
        _keep.Invoke<double>(false).ShouldBe(1);

        current.Dispose();
        Should.Throw<LuaException>(() => _keep.Invoke<double>(false)).Message.ShouldContain("was released");
    }

    [Fact]
    public void LongScope_KeepsNoRecordsOfUserdataLuauCollected()
    {
        using (_state.BeginUserdataScope())
        {
            for (int i = 0; i < 10_000; i++)
            {
                using LuauUserdata dropped = _state.GetOrCreateUserdata(new Point(i, i));
                if (i % 100 == 0)
                    _state.CollectGarbage();
            }

            _state.UserdataObjects.ScopeRecordCount.ShouldBeLessThan(1_000);
        }
    }

    private static InvalidOperationException? DisposeError(UserdataScope scope)
    {
        try
        {
            scope.Dispose();
            return null;
        }
        catch (InvalidOperationException e)
        {
            return e;
        }
    }

    public void Dispose()
    {
        _keep.Dispose();
        _state.Dispose();
    }
}
