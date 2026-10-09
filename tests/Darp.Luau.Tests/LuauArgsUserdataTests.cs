using Darp.Luau.Tests.Fixtures;
using Shouldly;

namespace Darp.Luau.Tests;

public sealed class LuauArgsUserdataTests : IDisposable
{
    private readonly LuauState _state = new();

    [Fact]
    public void Args_TryReadUserdata_ShouldResolveManagedInstance()
    {
        using LuauFunction func = _state.CreateFunctionManual(static args =>
        {
            if (!args.TryReadUserdata(1, out ValueUserdata? value, out string? error))
                return LuauReturn.Error(error);
            return LuauReturn.Ok(value.Value);
        });
        _state.Globals.Set("input", new ValueUserdata { Value = 42 });
        _state.Globals.Set("f", func);

        _state.Load("result = f(input)").Execute();
        _state.Globals.TryGet("result", out int result).ShouldBeTrue();
        result.ShouldBe(42);
    }

    [Fact]
    public void Args_TryReadUserdata_WhenTypeMismatches_ShouldFail()
    {
        using LuauFunction func = _state.CreateFunctionManual(static args =>
        {
            if (!args.TryReadUserdata<ValueUserdata>(1, out _, out string? error))
                return LuauReturn.Error(error);
            return LuauReturn.Ok();
        });
        _state.Globals.Set("input", new OtherValueUserdata());
        _state.Globals.Set("f", func);

        _state
            .Load(
                """
                ok, err = pcall(function()
                  f(input)
                end)
                """
            )
            .Execute();

        _state.Globals.TryGet("ok", out bool ok).ShouldBeTrue();
        ok.ShouldBeFalse();
        _state.Globals.TryGet("err", out string? err).ShouldBeTrue();
        err.ShouldContain("must be userdata of type");
    }

    [Fact]
    public void Args_TryReadUserdata_WhenValueIsNotUserdata_ShouldFail()
    {
        using LuauFunction func = _state.CreateFunctionManual(static args =>
        {
            if (!args.TryReadUserdata<ValueUserdata>(1, out _, out string? error))
                return LuauReturn.Error(error);
            return LuauReturn.Ok();
        });
        _state.Globals.Set("input", 12);
        _state.Globals.Set("f", func);

        _state
            .Load(
                """
                ok, err = pcall(function()
                  f(input)
                end)
                """
            )
            .Execute();

        _state.Globals.TryGet("ok", out bool ok).ShouldBeTrue();
        ok.ShouldBeFalse();
        _state.Globals.TryGet("err", out string? err).ShouldBeTrue();
        err.ShouldContain("LUA_TUSERDATA");
    }

    [Fact]
    public void Args_TryReadUserdataOrNil_ShouldAcceptNil()
    {
        using LuauFunction func = _state.CreateFunctionManual(static args =>
        {
            if (!args.TryReadUserdataOrNil(1, out ValueUserdata? value, out string? error))
                return LuauReturn.Error(error);
            return LuauReturn.Ok(value is null ? "nil" : "value");
        });
        _state.Globals.Set("f", func);

        _state.Load("result = f(nil)").Execute();
        _state.Globals.TryGet("result", out string? result).ShouldBeTrue();
        result.ShouldBe("nil");
    }

    [Fact]
    public void Args_TryReadUserdataOrNil_ShouldAcceptUserdata()
    {
        using LuauFunction func = _state.CreateFunctionManual(static args =>
        {
            if (!args.TryReadUserdataOrNil(1, out ValueUserdata? value, out string? error))
                return LuauReturn.Error(error);
            return LuauReturn.Ok(value is null ? "nil" : "value");
        });
        _state.Globals.Set("input", new ValueUserdata());
        _state.Globals.Set("f", func);

        _state.Load("result = f(input)").Execute();
        _state.Globals.TryGet("result", out string? result).ShouldBeTrue();
        result.ShouldBe("value");
    }

    public void Dispose()
    {
        _state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(1UL);
        _state.Dispose();
    }
}
