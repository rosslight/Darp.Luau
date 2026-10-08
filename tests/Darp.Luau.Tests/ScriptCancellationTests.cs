using Darp.Luau.Tests.Require;
using Shouldly;

namespace Darp.Luau.Tests;

/// <summary> Cancelling the token of an async host call stops the script, also where it never calls the host. </summary>
public sealed class ScriptCancellationTests : IDisposable
{
    private readonly LuauState _state = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly LuauFunction _cancel;

    public ScriptCancellationTests()
    {
        // Lets a script cancel its own host call, so that no test depends on timing.
        _cancel = _state.CreateFunctionBuilder(_ =>
        {
            _cts.Cancel();
            return LuauReturn.Ok();
        });
        _state.Globals.Set("cancel", _cancel);
    }

    private async Task ShouldBeCancelledAsync(string script)
    {
        OperationCanceledException exception = await Should.ThrowAsync<OperationCanceledException>(() =>
            _state.Load(script).ExecuteAsync([], _cts.Token).AsTask()
        );
        exception.CancellationToken.ShouldBe(_cts.Token);
    }

    [Fact]
    public async Task EndlessLoop_ShouldBeStoppedFromAnotherThread()
    {
        _cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await ShouldBeCancelledAsync("while true do end");
    }

    [Fact]
    public async Task Pcall_ShouldNotCatchTheCancellation()
    {
        await ShouldBeCancelledAsync(
            """
            while true do
                pcall(function()
                    entered = true
                    cancel()
                    while true do end
                end)
                returned = true
            end
            """
        );

        _state.Globals.GetBoolean("entered").ShouldBeTrue();
        _state.Globals.ContainsKey("returned").ShouldBeFalse();
    }

    [Fact]
    public async Task CancelledCall_ShouldLeaveTheStateUsable()
    {
        ulong baseline = _state.MemoryStatistics.ActiveRegistryReferences;

        await ShouldBeCancelledAsync("cancel() while true do end");

        _state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(baseline);
        _state.Load("local sum = 0 for i = 1, 10 do sum += i end return sum").Execute<int>().ShouldBe(55);
        (await _state.Load("return 1 + 1").ExecuteAsync<int>([], CancellationToken.None)).ShouldBe(2);
    }

    [Fact]
    public async Task CallWhoseTokenIsNotCancelled_ShouldRunToItsEnd()
    {
        int sum = await _state
            .Load("local sum = 0 for i = 1, 1000 do sum += i end return sum")
            .ExecuteAsync<int>([], _cts.Token);

        sum.ShouldBe(500500);
    }

    [Fact]
    public async Task CancelledResumeAsync_ShouldFinishTheCoroutine()
    {
        using LuauFunction body = _state.Load("cancel() while true do end").ToFunction();
        using LuauCoroutine coroutine = _state.CreateCoroutine(body);

        await Should.ThrowAsync<OperationCanceledException>(() => coroutine.ResumeAsync([], _cts.Token).AsTask());

        coroutine.Status.ShouldBe(LuauCoroutineStatus.Finished);
        Should.Throw<InvalidOperationException>(() => coroutine.Resume());
    }

    [Fact]
    public async Task EndlessLoop_InACoroutineOfTheScript_ShouldBeStopped()
    {
        await ShouldBeCancelledAsync(
            """
            local loop = coroutine.wrap(function()
                cancel()
                while true do end
            end)
            pcall(loop)
            returned = true
            """
        );

        _state.Globals.ContainsKey("returned").ShouldBeFalse();
    }

    /// <summary> The module is abandoned, which fails <c>require</c>. A script that catches that is stopped next. </summary>
    [Fact]
    public async Task EndlessLoop_InAScriptModule_ShouldBeStopped()
    {
        using LuauState state = CreateStateWithEndlessModule();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            state
                .Load(
                    """
                    module_loaded = pcall(require, "./endless")
                    while true do end
                    """
                )
                .ExecuteAsync([], _cts.Token)
                .AsTask()
        );

        state.Globals.GetBoolean("module_loaded").ShouldBeFalse();
    }

    /// <summary> Without a pcall, the failed <c>require</c> ends the script. The call is cancelled all the same. </summary>
    [Fact]
    public async Task EndlessLoop_InAScriptModuleRequiredWithoutPcall_ShouldCancelTheCall()
    {
        using LuauState state = CreateStateWithEndlessModule();

        OperationCanceledException exception = await Should.ThrowAsync<OperationCanceledException>(() =>
            state.Load("""return require("./endless")""").ExecuteAsync([], _cts.Token).AsTask()
        );

        exception.InnerException.ShouldBeOfType<LuaException>().Message.ShouldContain("was interrupted");
    }

    private LuauState CreateStateWithEndlessModule()
    {
        var fileSystem = new FakeFileSystem([("./endless.luau", "cancel() while true do end")]);
        var state = new LuauState(LuauLibraries.All, fileSystem);
        state.EnableScriptModules();
        // The state owns the callback: it is released with it.
        state.Globals.Set(
            "cancel",
            state.CreateFunctionBuilder(_ =>
            {
                _cts.Cancel();
                return LuauReturn.Ok();
            })
        );
        return state;
    }

    /// <summary> Such an error cannot be told from one that stopping the script caused where Luau cannot break. </summary>
    [Fact]
    public async Task ErrorThatEndsTheScriptAfterCancellation_ShouldBeReportedAsCancellation()
    {
        using LuauFunction body = _state.Load("cancel() local missing = nil return missing.key").ToFunction();
        using LuauCoroutine coroutine = _state.CreateCoroutine(body);

        OperationCanceledException exception = await Should.ThrowAsync<OperationCanceledException>(() =>
            coroutine.ResumeAsync([], _cts.Token).AsTask()
        );

        exception.CancellationToken.ShouldBe(_cts.Token);
        exception.InnerException.ShouldBeOfType<LuaException>().Message.ShouldContain("attempt to index nil");
        coroutine.Status.ShouldBe(LuauCoroutineStatus.Finished);
    }

    [Fact]
    public async Task ErrorOfACallWhoseTokenIsNotCancelled_ShouldStayALuaException()
    {
        await Should.ThrowAsync<LuaException>(() =>
            _state.Load("error('failed')").ExecuteAsync([], _cts.Token).AsTask()
        );
    }

    /// <summary> Luau cannot suspend a metamethod: the script is only stopped once the metamethod has returned. </summary>
    [Fact]
    public async Task Metamethod_ShouldRunToItsEndBeforeTheScriptIsStopped()
    {
        await ShouldBeCancelledAsync(
            """
            local proxy = setmetatable({}, { __index = function()
                cancel()
                for i = 1, 100 do end
                metamethod_returned = true
                return 1
            end })
            local value = proxy.key
            while true do end
            """
        );

        _state.Globals.GetBoolean("metamethod_returned").ShouldBeTrue();
    }

    /// <summary> A sync <c>Invoke</c> cannot be suspended either: it returns, then the script of the call is stopped. </summary>
    [Fact]
    public async Task SyncInvokeFromACallback_ShouldRunToItsEndBeforeTheScriptIsStopped()
    {
        using LuauFunction loop = _state
            .Load("cancel() local n = 0 for i = 1, 100 do n += 1 end return n")
            .ToFunction();
        int invokeResult = 0;
        using LuauFunction invokeLoop = _state.CreateFunctionBuilder(_ =>
        {
            invokeResult = loop.Invoke<int>();
            return LuauReturn.Ok();
        });
        _state.Globals.Set("invoke_loop", invokeLoop);

        await ShouldBeCancelledAsync("invoke_loop() while true do end");

        invokeResult.ShouldBe(100);
    }

    /// <summary> A host call without a token of its own is stopped by the token of the host call around it. </summary>
    [Fact]
    public async Task CallWithoutAToken_InsideACancelledCall_ShouldBeStoppedToo()
    {
        using LuauFunction endless = _state.Load("cancel() while true do end").ToFunction();
        OperationCanceledException? nestedCancellation = null;
        using LuauFunction runNested = _state.CreateFunctionBuilder(_ =>
        {
            // Completed when it returns: nothing in the script awaits.
            ValueTask nested = endless.InvokeAsync();
            nestedCancellation = Should.Throw<OperationCanceledException>(() => nested.GetAwaiter().GetResult());
            return LuauReturn.Ok();
        });
        _state.Globals.Set("run_nested", runNested);

        await ShouldBeCancelledAsync("run_nested() while true do end");

        nestedCancellation.ShouldNotBeNull().CancellationToken.ShouldBe(_cts.Token);
    }

    /// <summary> The nested call keeps the token of the call around it after that call was suspended. </summary>
    [Fact]
    public async Task CallWithoutAToken_InsideACancelledCall_ShouldBeStoppedAfterItAwaited()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken tokenInNestedCall = default;
        using LuauFunction wait = _state.CreateFunctionBuilder(args =>
        {
            tokenInNestedCall = args.CancellationToken;
            return args.TryGetAwaiter(out LuauAwaiter awaiter, out string? error)
                ? awaiter.Await(new ValueTask(gate.Task))
                : LuauReturn.Error(error);
        });
        _state.Globals.Set("wait", wait);
        using LuauFunction endless = _state.Load("wait() while true do end").ToFunction();
        using LuauFunction runNested = _state.CreateFunctionBuilder(args =>
            args.TryGetAwaiter(out LuauAwaiter awaiter, out string? error)
                ? awaiter.Await(endless.InvokeAsync())
                : LuauReturn.Error(error)
        );
        _state.Globals.Set("run_nested", runNested);

        ValueTask pending = _state.Load("run_nested()").ExecuteAsync([], _cts.Token);
        pending.IsCompleted.ShouldBeFalse();
        await _cts.CancelAsync();
        gate.SetResult();

        await Should.ThrowAsync<OperationCanceledException>(() => pending.AsTask());
        tokenInNestedCall.ShouldBe(_cts.Token);
    }

    /// <summary> A coroutine can be suspended, so the sync resume of a callback is stopped by the token around it. </summary>
    [Fact]
    public async Task SyncResumeFromACallback_ShouldBeStopped()
    {
        using LuauFunction endless = _state.Load("cancel() while true do end").ToFunction();
        using LuauCoroutine coroutine = _state.CreateCoroutine(endless);
        OperationCanceledException? nestedCancellation = null;
        using LuauFunction resume = _state.CreateFunctionBuilder(_ =>
        {
            nestedCancellation = Should.Throw<OperationCanceledException>(() => coroutine.Resume());
            return LuauReturn.Ok();
        });
        _state.Globals.Set("resume", resume);

        await ShouldBeCancelledAsync("resume() while true do end");

        nestedCancellation.ShouldNotBeNull().CancellationToken.ShouldBe(_cts.Token);
        coroutine.Status.ShouldBe(LuauCoroutineStatus.Finished);
    }

    public void Dispose()
    {
        _cancel.Dispose();
        _state.Dispose();
        _cts.Dispose();
    }
}
