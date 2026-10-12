using Darp.Luau.Tests.Fixtures;
using Shouldly;

namespace Darp.Luau.Tests;

public sealed class LuauArgsUserdataTests : IDisposable
{
    private readonly LuauState _state = new();

    [Fact]
    public void Args_Get_ShouldResolveManagedInstance()
    {
        using LuauFunction func = _state.CreateFunctionManual(static args =>
        {
            ValueUserdata value = args[1].Get<ValueUserdata>();
            return LuauReturn.Ok(value.Value);
        });
        _state.Globals.Set("input", new ValueUserdata { Value = 42 });
        _state.Globals.Set("f", func);

        _state.Load("result = f(input)").Execute();
        _state.Globals.TryGet("result", out int result).ShouldBeTrue();
        result.ShouldBe(42);
    }

    [Fact]
    public void Args_Get_WhenTypeMismatches_ShouldFail()
    {
        using LuauFunction func = _state.CreateFunctionManual(static args =>
        {
            _ = args[1].Get<ValueUserdata>();
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
        err.ShouldBe("invalid argument #1 (ValueUserdata expected, got userdata)");
    }

    [Fact]
    public void Args_Get_WhenValueIsNotUserdata_ShouldFail()
    {
        using LuauFunction func = _state.CreateFunctionManual(static args =>
        {
            _ = args[1].Get<ValueUserdata>();
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
        err.ShouldBe("invalid argument #1 (ValueUserdata expected, got number)");
    }

    [Fact]
    public void Args_GetOrNull_ShouldAcceptNil()
    {
        using LuauFunction func = _state.CreateFunctionManual(static args =>
        {
            ValueUserdata? value = args[1].GetOrNull<ValueUserdata>();
            return LuauReturn.Ok(value is null ? "nil" : "value");
        });
        _state.Globals.Set("f", func);

        _state.Load("result = f(nil)").Execute();
        _state.Globals.TryGet("result", out string? result).ShouldBeTrue();
        result.ShouldBe("nil");
    }

    [Fact]
    public void Args_GetOrNull_ShouldAcceptUserdata()
    {
        using LuauFunction func = _state.CreateFunctionManual(static args =>
        {
            ValueUserdata? value = args[1].GetOrNull<ValueUserdata>();
            return LuauReturn.Ok(value is null ? "nil" : "value");
        });
        _state.Globals.Set("input", new ValueUserdata());
        _state.Globals.Set("f", func);

        _state.Load("result = f(input)").Execute();
        _state.Globals.TryGet("result", out string? result).ShouldBeTrue();
        result.ShouldBe("value");
    }

    [Fact]
    public void Args_TypeAndTypeName_ShouldTellTheTypeOfEveryArgumentAndNilPastTheLast()
    {
        using LuauFunction func = _state.CreateFunctionManual(static args =>
        {
            var types = new List<string>();
            for (int index = 1; index <= args.Count + 1; index++)
                types.Add($"{args[index].Type}={args[index].TypeName}");
            return LuauReturn.Ok(string.Join(' ', types));
        });
        using LuauBuffer bytes = _state.CreateBuffer([1]);
        _state.Globals.Set("input", new ValueUserdata());
        _state.Globals.Set("bytes", bytes);
        _state.Globals.Set("f", func);

        string types = _state
            .Load("return f(nil, true, 1, 'a', {}, f, coroutine.create(f), input, vector.create(1, 2, 3), bytes)")
            .Execute<string>();

        types.ShouldBe(
            "Nil=nil Boolean=boolean Number=number String=string Table=table Function=function Thread=thread "
                + "Userdata=userdata Vector=vector Buffer=buffer Nil=nil"
        );
    }

    [Fact]
    public void Args_TryGet_ShouldOnlyAcceptTheManagedTypeOfTheArgument()
    {
        using LuauFunction func = _state.CreateFunctionManual(static args =>
            LuauReturn.Ok(
                string.Join(
                    ' ',
                    args[1].TryGet<ValueUserdata>(out _),
                    args[1].TryGet<OtherValueUserdata>(out _),
                    args[2].TryGet<ValueUserdata>(out _),
                    args[3].TryGet<ValueUserdata>(out _)
                )
            )
        );
        _state.Globals.Set("input", new ValueUserdata());
        _state.Globals.Set("f", func);

        // A value of the type, the same value read as another type, a number, and no argument at all.
        _state.Load("return f(input, 12)").Execute<string>().ShouldBe("True False False False");
    }

    public void Dispose()
    {
        _state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(1UL);
        _state.Dispose();
    }
}
