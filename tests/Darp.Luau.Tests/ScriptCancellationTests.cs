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
        using var loopStarted = new ManualResetEventSlim();
        using LuauFunction started = _state.CreateFunctionBuilder(_ =>
        {
            loopStarted.Set();
            return LuauReturn.Ok();
        });
        _state.Globals.Set("started", started);
        Task cancellation = Task.Run(
            () =>
            {
                loopStarted.Wait(TestContext.Current.CancellationToken);
                _cts.Cancel();
            },
            TestContext.Current.CancellationToken
        );

        await ShouldBeCancelledAsync("started() while true do end");
        await cancellation;
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

        await Should.ThrowAsync<OperationCanceledException>(() =>
            state.Load("""return require("./endless")""").ExecuteAsync([], _cts.Token).AsTask()
        );
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
        coroutine.Status.ShouldBe(LuauCoroutineStatus.Finished);
    }

    [Fact]
    public async Task ErrorOfACallWhoseTokenIsNotCancelled_ShouldStayALuaException()
    {
        await Should.ThrowAsync<LuaException>(() =>
            _state.Load("error('failed')").ExecuteAsync([], _cts.Token).AsTask()
        );
    }

    /// <summary> Luau cannot suspend these places. There the script is stopped with an error that ends it. </summary>
    [Theory]
    [InlineData("local value = setmetatable({}, { __index = function() cancel() while true do end end }).key")]
    [InlineData("table.sort({ 3, 2, 1 }, function() cancel() while true do end end)")]
    [InlineData("string.gsub('a', 'a', function() cancel() while true do end end)")]
    [InlineData("xpcall(error, function() cancel() while true do end end)")]
    public async Task EndlessLoop_WhereLuauCannotYield_ShouldBeStopped(string script)
    {
        await ShouldBeCancelledAsync(script);
    }

    /// <summary> The script can catch the error, but it is stopped again at its next safepoint. </summary>
    [Fact]
    public async Task ErrorThatStopsAMetamethod_ShouldNotLetTheScriptGoOnAfterItCaughtIt()
    {
        await ShouldBeCancelledAsync(
            """
            local proxy = setmetatable({}, { __index = function() cancel() while true do end end })
            local ok, message = pcall(function() return proxy.key end)
            caught = message
            iterations = 0
            while true do
                iterations += 1
            end
            """
        );

        _state.Globals.GetUtf8String("caught").ShouldContain("script was interrupted");
        // What follows runs up to the next safepoint, the end of the first iteration.
        _state.Globals.GetNumber("iterations").ShouldBe(1);
    }

    /// <summary> A sync host call takes no token: it runs under the token of the async call that runs the callback. </summary>
    [Fact]
    public async Task SyncInvokeFromACallback_ShouldBeStopped()
    {
        using LuauFunction endless = _state.Load("cancel() while true do end").ToFunction();
        OperationCanceledException? nestedCancellation = null;
        using LuauFunction invokeEndless = _state.CreateFunctionBuilder(_ =>
        {
            nestedCancellation = Should.Throw<OperationCanceledException>(() => endless.Invoke());
            return LuauReturn.Ok();
        });
        _state.Globals.Set("invoke_endless", invokeEndless);

        await ShouldBeCancelledAsync("invoke_endless() while true do end");

        nestedCancellation.ShouldNotBeNull().CancellationToken.ShouldBe(_cts.Token);
    }

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

    /// <summary> The conversion of an awaited result runs after the script was suspended. It is part of the call all the same. </summary>
    [Fact]
    public async Task SyncInvokeFromTheConversionOfAnAwaitedResult_ShouldBeStopped()
    {
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using LuauFunction endless = _state.Load("while true do end").ToFunction();
        using LuauFunction convert = _state.CreateFunctionBuilder(args =>
            args.TryGetAwaiter(out LuauAwaiter awaiter, out string? error)
                ? awaiter.Await(
                    new ValueTask<int>(gate.Task),
                    _ =>
                    {
                        endless.Invoke();
                        return LuauReturn.Ok();
                    }
                )
                : LuauReturn.Error(error)
        );
        _state.Globals.Set("convert", convert);

        ValueTask pending = _state.Load("convert()").ExecuteAsync([], _cts.Token);
        await _cts.CancelAsync();
        gate.SetResult(1);

        await Should.ThrowAsync<OperationCanceledException>(() => pending.AsTask());
    }

    /// <summary> Every host call is stopped by its own token only: the callback has to pass the token on. </summary>
    [Fact]
    public async Task AsyncCallFromACallback_ShouldBeStoppedByTheTokenPassedToIt_AlsoAfterItAwaited()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using LuauFunction wait = _state.CreateFunctionBuilder(args =>
            args.TryGetAwaiter(out LuauAwaiter awaiter, out string? error)
                ? awaiter.Await(new ValueTask(gate.Task))
                : LuauReturn.Error(error)
        );
        _state.Globals.Set("wait", wait);
        using LuauFunction endless = _state.Load("wait() while true do end").ToFunction();
        using LuauFunction runNested = _state.CreateFunctionBuilder(args =>
            args.TryGetAwaiter(out LuauAwaiter awaiter, out string? error)
                ? awaiter.Await(endless.InvokeAsync([], args.CancellationToken))
                : LuauReturn.Error(error)
        );
        _state.Globals.Set("run_nested", runNested);

        ValueTask pending = _state.Load("run_nested()").ExecuteAsync([], _cts.Token);
        pending.IsCompleted.ShouldBeFalse();
        await _cts.CancelAsync();
        gate.SetResult();

        await Should.ThrowAsync<OperationCanceledException>(() => pending.AsTask());
    }

    [Fact]
    public async Task AsyncCallWithoutAToken_InsideACancelledCall_ShouldRunToItsEnd()
    {
        using LuauFunction count = _state
            .Load("cancel() local n = 0 for i = 1, 100 do n += 1 end return n")
            .ToFunction();
        double counted = 0;
        using LuauFunction runNested = _state.CreateFunctionBuilder(_ =>
        {
            // Completed when it returns: nothing in the script awaits.
            counted = count.InvokeAsync<double>().GetAwaiter().GetResult();
            return LuauReturn.Ok();
        });
        _state.Globals.Set("run_nested", runNested);

        await ShouldBeCancelledAsync("run_nested() while true do end");

        counted.ShouldBe(100);
    }

    /// <summary> A call that continues inside a callback of another call is not stopped by the token of that call. </summary>
    [Fact]
    public async Task CallContinuingInsideACallbackOfACancelledCall_ShouldNotBeStopped()
    {
        // Continuations run inline, inside the callback that completes the gate.
        var gate = new TaskCompletionSource();
        using LuauFunction wait = _state.CreateFunctionBuilder(args =>
            args.TryGetAwaiter(out LuauAwaiter awaiter, out string? error)
                ? awaiter.Await(new ValueTask(gate.Task))
                : LuauReturn.Error(error)
        );
        _state.Globals.Set("wait", wait);
        using LuauFunction open = _state.CreateFunctionBuilder(_ =>
        {
            _cts.Cancel();
            gate.SetResult();
            return LuauReturn.Ok();
        });
        _state.Globals.Set("open", open);

        ValueTask<int> waiting = _state
            .Load("wait() local n = 0 for i = 1, 100 do n += 1 end return n")
            .ExecuteAsync<int>([], CancellationToken.None);
        await ShouldBeCancelledAsync("open() while true do end");

        (await waiting).ShouldBe(100);
    }

    /// <summary> A function of Luau that yields is entered without a safepoint. The failed call is cancelled all the same. </summary>
    [Fact]
    public async Task YieldOutOfACancelledInvocation_ShouldBeReportedAsCancellation()
    {
        using LuauTable coroutineLibrary = _state.Globals.GetLuauTable("coroutine");
        using LuauFunction yield = coroutineLibrary.GetLuauFunction("yield");
        await _cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => yield.InvokeAsync([], _cts.Token).AsTask());
    }

    public void Dispose()
    {
        _cancel.Dispose();
        _state.Dispose();
        _cts.Dispose();
    }
}
