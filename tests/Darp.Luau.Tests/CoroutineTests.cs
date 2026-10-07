using Shouldly;

namespace Darp.Luau.Tests;

public sealed class CoroutineTests : IDisposable
{
    private readonly LuauState _state = new();

    private LuauCoroutine CreateCoroutine(string body)
    {
        using LuauFunction function = _state.Load(body).ToFunction();
        return _state.CreateCoroutine(function);
    }

    [Fact]
    public void Resume_ShouldReturnYieldedValuesAndThenFinalResults()
    {
        using LuauCoroutine coroutine = CreateCoroutine(
            """
            local start = ...
            local received = coroutine.yield(start + 1)
            return received * 2
            """
        );

        coroutine.Resume<int>(1).ShouldBe(2);
        coroutine.Resume<int>(10).ShouldBe(20);
    }

    [Fact]
    public void Status_ShouldFollowTheLifecycle()
    {
        using LuauCoroutine coroutine = CreateCoroutine("coroutine.yield()");

        coroutine.Status.ShouldBe(LuauCoroutineStatus.Suspended);
        coroutine.Resume();
        coroutine.Status.ShouldBe(LuauCoroutineStatus.Suspended);
        coroutine.Resume();
        coroutine.Status.ShouldBe(LuauCoroutineStatus.Finished);
    }

    [Fact]
    public void Status_InsideTheCoroutine_ShouldBeRunning()
    {
        LuauCoroutine coroutine = default;
        using LuauFunction readStatus = _state.CreateFunctionBuilder(_ => LuauReturn.Ok(coroutine.Status.ToString()));
        _state.Globals.Set("read_status", readStatus);
        coroutine = CreateCoroutine("return read_status()");
        using (coroutine)
        {
            coroutine.Resume<string>().ShouldBe(nameof(LuauCoroutineStatus.Running));
        }
    }

    [Fact]
    public void ResumeMulti_ShouldReturnAllYieldedValues()
    {
        using LuauCoroutine coroutine = CreateCoroutine("coroutine.yield(1, 'two', true)");

        LuauValue[] values = coroutine.ResumeMulti();

        values.Length.ShouldBe(3);
        values[0].TryGet(out int first).ShouldBeTrue();
        first.ShouldBe(1);
        values[1].TryGet(out string? second).ShouldBeTrue();
        second.ShouldBe("two");
        values[2].TryGet(out bool third).ShouldBeTrue();
        third.ShouldBeTrue();
        foreach (LuauValue value in values)
            value.Dispose();
    }

    [Fact]
    public void Resume_AfterFinished_ShouldThrowInvalidOperationException()
    {
        using LuauCoroutine coroutine = CreateCoroutine("return 1");
        coroutine.Resume<int>().ShouldBe(1);

        Should.Throw<InvalidOperationException>(() => coroutine.Resume());
        coroutine.Status.ShouldBe(LuauCoroutineStatus.Finished);
    }

    [Fact]
    public void Resume_WhenTheCoroutineErrors_ShouldThrowLuaExceptionAndEndInError()
    {
        using LuauCoroutine coroutine = CreateCoroutine("error('boom inside coroutine')");

        LuaException exception = Should.Throw<LuaException>(() => coroutine.Resume());

        exception.Message.ShouldContain("boom inside coroutine");
        coroutine.Status.ShouldBe(LuauCoroutineStatus.Error);
        Should.Throw<InvalidOperationException>(() => coroutine.Resume());
    }

    [Fact]
    public void ScriptCreatedCoroutine_ShouldBeResumableByTheHost()
    {
        _state
            .Load(
                """
                co = coroutine.create(function(value)
                    coroutine.yield(value)
                    return "done"
                end)
                """
            )
            .Execute();

        using LuauCoroutine coroutine = _state.Globals.GetLuauCoroutine("co");

        coroutine.Resume<int>(5).ShouldBe(5);
        coroutine.Resume<string>().ShouldBe("done");
        coroutine.Status.ShouldBe(LuauCoroutineStatus.Finished);
    }

    [Fact]
    public void HostCreatedCoroutine_ShouldBeResumableByAScript()
    {
        using LuauCoroutine coroutine = CreateCoroutine(
            """
            local value = ...
            coroutine.yield(value * 3)
            """
        );
        _state.Globals.Set("co", coroutine);

        (bool ok, int value, string status) = _state
            .Load(
                """
                local ok, value = coroutine.resume(co, 2)
                return ok, value, coroutine.status(co)
                """
            )
            .Execute<bool, int, string>();

        ok.ShouldBeTrue();
        value.ShouldBe(6);
        status.ShouldBe("suspended");
        coroutine.Status.ShouldBe(LuauCoroutineStatus.Suspended);
    }

    [Fact]
    public void LuauValue_OfACoroutine_ShouldConvertToLuauCoroutine()
    {
        _state.Load("co = coroutine.create(function() return 7 end)").Execute();

        using LuauValue value = _state.Globals.GetLuauValue("co");
        value.Type.ShouldBe(LuauValueType.Thread);
        value.TryGet(out LuauCoroutine coroutine).ShouldBeTrue();
        using (coroutine)
        {
            coroutine.Resume<int>().ShouldBe(7);
        }
    }

    [Fact]
    public void TryReadLuauCoroutine_ToOwned_ShouldOutliveTheCallback()
    {
        LuauCoroutine stored = default;
        using LuauFunction store = _state.CreateFunctionBuilder(args =>
        {
            if (!args.TryReadLuauCoroutine(1, out LuauCoroutineView view, out string? error))
                return LuauReturn.Error(error);
            stored = view.ToOwned();
            return LuauReturn.Ok();
        });
        _state.Globals.Set("store", store);

        _state.Load("store(coroutine.create(function() return 'later' end))").Execute();

        using (stored)
        {
            stored.Resume<string>().ShouldBe("later");
        }
    }

    [Fact]
    public void TryReadLuauCoroutine_WithAFunction_ShouldFail()
    {
        string? readError = null;
        using LuauFunction read = _state.CreateFunctionBuilder(args =>
        {
            _ = args.TryReadLuauCoroutine(1, out _, out readError);
            return LuauReturn.Ok();
        });
        _state.Globals.Set("read", read);

        _state.Load("read(print)").Execute();

        readError.ShouldNotBeNull();
        readError.ShouldContain("LUA_TTHREAD");
    }

    [Fact]
    public void Dispose_ShouldReleaseTheRegistryReference()
    {
        using LuauFunction body = _state.Load("coroutine.yield(1)").ToFunction();
        ulong baseline = _state.MemoryStatistics.ActiveRegistryReferences;

        LuauCoroutine coroutine = _state.CreateCoroutine(body);
        _state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(baseline + 1);
        coroutine.Resume<int>().ShouldBe(1);
        _state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(baseline + 1);

        coroutine.Dispose();

        coroutine.IsDisposed.ShouldBeTrue();
        _state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(baseline);
        Should.Throw<ObjectDisposedException>(() => coroutine.Resume());
    }

    [Fact]
    public void CreateCoroutine_WithAFunctionOfAnotherState_ShouldThrow()
    {
        using var otherState = new LuauState();
        using LuauFunction foreign = otherState.Load("return 1").ToFunction();

        Should.Throw<InvalidOperationException>(() => _state.CreateCoroutine(foreign));
    }

    public void Dispose() => _state.Dispose();
}
