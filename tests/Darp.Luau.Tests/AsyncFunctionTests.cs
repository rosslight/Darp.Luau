using Shouldly;

namespace Darp.Luau.Tests;

public sealed class AsyncFunctionTests : IDisposable
{
    private const string AwaitRejectedMessage = "async managed callback requires an async host invocation";

    private readonly LuauState _state = new();
    private int _startedWorkCount;
    private readonly TaskCompletionSource<LuauReturn> _never = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    /// <summary> Registers a global callback that awaits <paramref name="work"/> with its first argument. </summary>
    private LuauFunction SetAsyncGlobal(string name, Func<double, CancellationToken, ValueTask<LuauReturn>> work)
    {
        LuauFunction function = _state.CreateFunctionBuilder(args =>
        {
            double value = args.ArgumentCount > 0 && args.TryReadNumber(1, out double number, out _) ? number : 0;
            if (!args.TryGetAwaiter(out LuauAwaiter awaiter, out string? error))
                return LuauReturn.Error(error);
            _startedWorkCount++;
            return awaiter.Await(work(value, args.CancellationToken));
        });
        _state.Globals.Set(name, function);
        return function;
    }

    /// <summary> Runs full Luau garbage collections and reuses the memory they freed. </summary>
    private void CollectGarbage() =>
        _state
            .Load(
                """
                -- A script cannot request a collection: allocate until two generations of unreferenced tables are gone.
                local function addUnreferenced(weak)
                    weak[1] = {}
                end
                for generation = 1, 2 do
                    local weak = setmetatable({}, { __mode = "v" })
                    addUnreferenced(weak)
                    local allocations = 0
                    while weak[1] ~= nil do
                        allocations += 1
                        assert(allocations < 10000000, "no collection ran")
                        local garbage = { coroutine.create(function() end) }
                    end
                end
                """
            )
            .Execute();

    private static async ValueTask<LuauReturn> AddLater(Task<int> increment, double value) =>
        LuauReturn.Ok(value + await increment);

    private static async ValueTask<LuauReturn> YieldThen(LuauReturn result)
    {
        await Task.Yield();
        return result;
    }

    private static async ValueTask<LuauReturn> WaitForCancellation(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return LuauReturn.Ok();
    }

    [Fact]
    public async Task ExecuteAsync_WithoutAwaitingCallbacks_ShouldCompleteSynchronously()
    {
        ValueTask<int> pending = _state.Load("return 40 + 2").ExecuteAsync<int>([], TestToken);

        pending.IsCompletedSuccessfully.ShouldBeTrue();
        (await pending).ShouldBe(42);
    }

    [Fact]
    public async Task Await_OfAlreadyCompletedWork_ShouldNotSuspend()
    {
        using LuauFunction next = SetAsyncGlobal(
            "next_number",
            (value, _) => ValueTask.FromResult(LuauReturn.Ok(value + 1))
        );

        ValueTask<int> pending = _state.Load("return next_number(41)").ExecuteAsync<int>([], TestToken);

        pending.IsCompletedSuccessfully.ShouldBeTrue();
        (await pending).ShouldBe(42);
    }

    [Fact]
    public async Task Await_ShouldSuspendTheScriptAndResumeItWithTheResult()
    {
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using LuauFunction addLater = SetAsyncGlobal("add_later", (value, _) => AddLater(gate.Task, value));

        ValueTask<int> pending = _state.Load("return add_later(40)").ExecuteAsync<int>([], TestToken);

        pending.IsCompleted.ShouldBeFalse();
        gate.SetResult(2);
        (await pending).ShouldBe(42);
    }

    [Fact]
    public async Task Await_InALoop_ShouldResumeAfterEveryAwait()
    {
        using LuauFunction echo = SetAsyncGlobal("echo", (value, _) => YieldThen(LuauReturn.Ok(value)));

        int sum = await _state
            .Load(
                """
                local sum = 0
                for i = 1, 5 do
                    sum = sum + echo(i)
                end
                return sum + echo(echo(100))
                """
            )
            .ExecuteAsync<int>([], TestToken);

        sum.ShouldBe(115);
    }

    [Fact]
    public async Task IndependentInvocations_ShouldBothBeSuspendedAndResumeInAnyOrder()
    {
        TaskCompletionSource<int>[] gates =
        [
            new(TaskCreationOptions.RunContinuationsAsynchronously),
            new(TaskCreationOptions.RunContinuationsAsynchronously),
        ];
        using LuauFunction addLater = SetAsyncGlobal(
            "add_later",
            (value, _) => AddLater(gates[_startedWorkCount - 1].Task, value)
        );

        ValueTask<int> first = _state.Load("return add_later(10)").ExecuteAsync<int>([], TestToken);
        ValueTask<int> second = _state.Load("return add_later(20)").ExecuteAsync<int>([], TestToken);
        first.IsCompleted.ShouldBeFalse();
        second.IsCompleted.ShouldBeFalse();

        gates[1].SetResult(22);
        (await second).ShouldBe(42);
        first.IsCompleted.ShouldBeFalse();

        gates[0].SetResult(32);
        (await first).ShouldBe(42);
    }

    [Fact]
    public async Task ManySuspendedInvocations_ShouldEachKeepTheirOwnTokenAndResult()
    {
        const int invocationCount = 10;
        var gates = new TaskCompletionSource<int>[invocationCount];
        var sources = new CancellationTokenSource[invocationCount];
        var observedTokens = new CancellationToken[invocationCount];
        using LuauFunction addLater = SetAsyncGlobal(
            "add_later",
            (value, _) => AddLater(gates[(int)value].Task, value)
        );
        using LuauFunction readToken = _state.CreateFunctionBuilder(args =>
        {
            args.TryReadNumber(1, out double index, out _).ShouldBeTrue();
            observedTokens[(int)index] = args.CancellationToken;
            return LuauReturn.Ok();
        });
        _state.Globals.Set("read_token", readToken);
        using LuauFunction body = _state
            .Load(
                """
                local index = ...
                local result = add_later(index)
                read_token(index)
                return result
                """
            )
            .ToFunction();

        // Two rounds, so that the second one reuses what the first one left behind.
        for (int round = 0; round < 2; round++)
        {
            var pending = new Task<int>[invocationCount];
            for (int i = 0; i < invocationCount; i++)
            {
                gates[i] = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                sources[i] = new CancellationTokenSource();
                pending[i] = body.InvokeAsync<int>([i], sources[i].Token).AsTask();
                pending[i].IsCompleted.ShouldBeFalse();
            }

            for (int i = invocationCount - 1; i >= 0; i--)
            {
                gates[i].SetResult(100);
                (await pending[i]).ShouldBe(100 + i);
                observedTokens[i].ShouldBe(sources[i].Token);
                sources[i].Dispose();
            }
        }
    }

    [Fact]
    public async Task FaultedWork_ShouldBeCatchableByScriptPcall()
    {
        using LuauFunction explode = SetAsyncGlobal(
            "explode",
            async (_, _) =>
            {
                await Task.Yield();
                throw new InvalidOperationException("boom from async callback");
            }
        );

        (bool ok, string error) = await _state
            .Load(
                """
                local ok, err = pcall(explode)
                return ok, tostring(err)
                """
            )
            .ExecuteAsync<bool, string>([], TestToken);

        ok.ShouldBeFalse();
        error.ShouldContain("managed function callback failed");
        error.ShouldContain("boom from async callback");
    }

    [Fact]
    public async Task FaultedWork_WhenUnhandled_ShouldFailTheInvocationWithLuaException()
    {
        using LuauFunction explode = SetAsyncGlobal(
            "explode",
            async (_, _) =>
            {
                await Task.Yield();
                throw new InvalidOperationException("boom from async callback");
            }
        );

        LuaException exception = await Should.ThrowAsync<LuaException>(() =>
            _state.Load("explode()").ExecuteAsync([], TestToken).AsTask()
        );

        exception.Message.ShouldContain("boom from async callback");
    }

    [Fact]
    public async Task ErrorResult_AfterAwait_ShouldBeALuaError()
    {
        using LuauFunction fail = SetAsyncGlobal("fail", (_, _) => YieldThen(LuauReturn.Error("async failure")));

        string error = await _state
            .Load(
                """
                local ok, err = pcall(fail)
                return tostring(err)
                """
            )
            .ExecuteAsync<string>([], TestToken);

        error.ShouldContain("async failure");
    }

    [Fact]
    public void TryGetAwaiter_UnderSyncExecute_ShouldBeRefusedBeforeAnyWorkStarts()
    {
        using LuauFunction wait = SetAsyncGlobal("wait", (_, _) => ValueTask.FromResult(LuauReturn.Ok()));

        (bool ok, string error) = _state
            .Load(
                """
                local ok, err = pcall(wait)
                return ok, tostring(err)
                """
            )
            .Execute<bool, string>();

        ok.ShouldBeFalse();
        error.ShouldContain(AwaitRejectedMessage);
        _startedWorkCount.ShouldBe(0);
    }

    [Fact]
    public async Task TryGetAwaiter_WhereLuauCannotYield_ShouldBeRefusedBeforeAnyWorkStarts()
    {
        using LuauFunction compare = SetAsyncGlobal("compare", (_, _) => ValueTask.FromResult(LuauReturn.Ok(true)));

        (bool ok, string error) = await _state
            .Load(
                """
                local ok, err = pcall(table.sort, { 2, 1 }, compare)
                return ok, tostring(err)
                """
            )
            .ExecuteAsync<bool, string>([], TestToken);

        ok.ShouldBeFalse();
        error.ShouldContain("Luau cannot yield across this call");
        _startedWorkCount.ShouldBe(0);
    }

    [Fact]
    public async Task TryGetAwaiter_InACallbackInvokedSynchronouslyByACallback_ShouldBeRefused()
    {
        using LuauFunction inner = SetAsyncGlobal("inner", (_, _) => ValueTask.FromResult(LuauReturn.Ok()));
        using LuauFunction callInner = _state.Load("local ok, err = pcall(inner) return tostring(err)").ToFunction();
        using LuauFunction bridge = _state.CreateFunctionBuilder(_ => LuauReturn.Ok(callInner.Invoke<string>()));
        _state.Globals.Set("bridge", bridge);

        string error = await _state.Load("return bridge()").ExecuteAsync<string>([], TestToken);

        error.ShouldContain(AwaitRejectedMessage);
        _startedWorkCount.ShouldBe(0);
    }

    [Fact]
    public void Awaiter_ThatWasNotGranted_ShouldRefuseToAwait()
    {
        Should.Throw<InvalidOperationException>(() => default(LuauAwaiter).Await(ValueTask.CompletedTask));
    }

    [Fact]
    public void PendingResult_ReturnedWhereItCannotBeAwaited_ShouldBeALuaError()
    {
        LuauReturn pendingElsewhere = default;
        using LuauFunction grant = _state.CreateFunctionBuilder(args =>
        {
            if (!args.TryGetAwaiter(out LuauAwaiter awaiter, out string? error))
                return LuauReturn.Error(error);
            pendingElsewhere = awaiter.Await(new ValueTask<LuauReturn>(_never.Task));
            return LuauReturn.Ok();
        });
        using LuauFunction replay = _state.CreateFunctionBuilder(_ => pendingElsewhere);
        _state.Globals.Set("replay", replay);
        grant.InvokeAsync([], TestToken).IsCompletedSuccessfully.ShouldBeTrue();

        (bool ok, string error) = _state
            .Load(
                """
                local ok, err = pcall(replay)
                return ok, tostring(err)
                """
            )
            .Execute<bool, string>();

        ok.ShouldBeFalse();
        error.ShouldContain(AwaitRejectedMessage);
    }

    [Fact]
    public async Task Await_InsideScriptCoroutine_ShouldBeALuaError()
    {
        using LuauFunction wait = SetAsyncGlobal("wait", (_, _) => new ValueTask<LuauReturn>(_never.Task));

        (bool ok, string error) = await _state
            .Load(
                """
                local ok, err = pcall(coroutine.wrap(function() return wait() end))
                return ok, tostring(err)
                """
            )
            .ExecuteAsync<bool, string>([], TestToken);

        ok.ShouldBeFalse();
        error.ShouldContain(AwaitRejectedMessage);
    }

    [Fact]
    public async Task Await_ReturningPendingWork_ShouldBeALuaError()
    {
        using LuauFunction nested = _state.CreateFunctionBuilder(args =>
        {
            if (!args.TryGetAwaiter(out LuauAwaiter awaiter, out string? error))
                return LuauReturn.Error(error);
            LuauReturn inner = awaiter.Await(new ValueTask<LuauReturn>(_never.Task));
            return awaiter.Await(YieldThen(inner));
        });
        _state.Globals.Set("nested", nested);

        string error = await _state
            .Load(
                """
                local ok, err = pcall(nested)
                return tostring(err)
                """
            )
            .ExecuteAsync<string>([], TestToken);

        error.ShouldContain("nested await");
    }

    [Fact]
    public async Task Work_AfterItsAwait_MayCallIntoLuauAndReturnOwnedReferences()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        LuauState state = _state;
        using LuauFunction callLater = _state.CreateFunctionBuilder(args =>
        {
            if (!args.TryReadLuauFunction(1, out LuauFunctionView callbackView, out string? error))
                return LuauReturn.Error(error);
            if (!args.TryGetAwaiter(out LuauAwaiter awaiter, out error))
                return LuauReturn.Error(error);
            return awaiter.Await(CallLater(state, callbackView.ToOwned(), gate.Task));

            static async ValueTask<LuauReturn> CallLater(LuauState state, LuauFunction callback, Task gate)
            {
                using (callback)
                {
                    await gate;
                    using LuauTable table = state.CreateTable();
                    table.Set("value", callback.Invoke<int>(41));
                    return LuauReturn.Ok(table);
                }
            }
        });
        _state.Globals.Set("call_later", callLater);
        ulong baseline = _state.MemoryStatistics.ActiveRegistryReferences;

        ValueTask<int> pending = _state
            .Load("return call_later(function(x) return x + 1 end).value")
            .ExecuteAsync<int>([], TestToken);
        pending.IsCompleted.ShouldBeFalse();
        gate.SetResult();

        (await pending).ShouldBe(42);
        _state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(baseline);
    }

    [Fact]
    public async Task CancellationToken_ShouldFlowIntoTheCallbackArguments()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken observed = default;
        using LuauFunction readToken = SetAsyncGlobal(
            "read_token",
            (_, cancellationToken) =>
            {
                observed = cancellationToken;
                return ValueTask.FromResult(LuauReturn.Ok());
            }
        );

        await _state.Load("read_token()").ExecuteAsync([], cts.Token);
        observed.ShouldBe(cts.Token);

        using LuauFunction readTokenSync = _state.CreateFunctionBuilder(args =>
        {
            observed = args.CancellationToken;
            return LuauReturn.Ok();
        });
        readTokenSync.Invoke();
        observed.ShouldBe(CancellationToken.None);
    }

    [Fact]
    public async Task Cancellation_ShouldCancelTheInvocationEvenUnderPcallAndKeepTheStateUsable()
    {
        using var cts = new CancellationTokenSource();
        using LuauFunction wait = SetAsyncGlobal(
            "wait",
            (_, cancellationToken) => WaitForCancellation(cancellationToken)
        );
        ulong baseline = _state.MemoryStatistics.ActiveRegistryReferences;

        ValueTask pending = _state.Load("pcall(wait) reached = true").ExecuteAsync([], cts.Token);
        pending.IsCompleted.ShouldBeFalse();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => pending.AsTask());
        _state.Globals.ContainsKey("reached").ShouldBeFalse();
        _state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(baseline);
        _state.Load("return 1 + 1").Execute<int>().ShouldBe(2);
    }

    [Fact]
    public async Task Cancellation_IsCooperative_WorkIgnoringTheTokenStillDeliversItsResult()
    {
        using var cts = new CancellationTokenSource();
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using LuauFunction addLater = SetAsyncGlobal("add_later", (value, _) => AddLater(gate.Task, value));

        ValueTask<int> pending = _state.Load("return add_later(40)").ExecuteAsync<int>([], cts.Token);
        await cts.CancelAsync();
        pending.IsCompleted.ShouldBeFalse();
        gate.SetResult(2);

        (await pending).ShouldBe(42);
    }

    [Fact]
    public async Task WorkCancelledByAnotherToken_ShouldBeALuaErrorCatchableByPcall()
    {
        using var unrelated = new CancellationTokenSource();
        await unrelated.CancelAsync();
        using LuauFunction wait = SetAsyncGlobal(
            "wait",
            async (_, _) =>
            {
                await Task.Yield();
                throw new OperationCanceledException("timed out", unrelated.Token);
            }
        );

        (bool ok, string error) = await _state
            .Load(
                """
                local ok, err = pcall(wait)
                return ok, tostring(err)
                """
            )
            .ExecuteAsync<bool, string>([], TestToken);

        ok.ShouldBeFalse();
        error.ShouldContain("timed out");
    }

    [Fact]
    public async Task ConversionThrowingOperationCanceled_AfterSuspension_ShouldBeALuaErrorLikeBeforeIt()
    {
        using var cts = new CancellationTokenSource();
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using LuauFunction convert = _state.CreateFunctionBuilder(args =>
            args.TryGetAwaiter(out LuauAwaiter awaiter, out string? error)
                ? awaiter.Await(
                    new ValueTask<int>(gate.Task),
                    static _ => throw new OperationCanceledException("conversion gave up")
                )
                : LuauReturn.Error(error)
        );
        _state.Globals.Set("convert", convert);

        ValueTask<string> pending = _state
            .Load("local ok, err = pcall(convert) return tostring(err)")
            .ExecuteAsync<string>([], cts.Token);
        await cts.CancelAsync();
        gate.SetResult(1);

        (await pending).ShouldContain("conversion gave up");
    }

    [Fact]
    public async Task Cancellation_OfResumeAsync_ShouldFinishTheCoroutine()
    {
        using var cts = new CancellationTokenSource();
        using LuauFunction wait = SetAsyncGlobal(
            "wait",
            (_, cancellationToken) => WaitForCancellation(cancellationToken)
        );
        using LuauFunction body = _state.Load("wait()").ToFunction();
        using LuauCoroutine coroutine = _state.CreateCoroutine(body);

        ValueTask pending = coroutine.ResumeAsync([], cts.Token);
        pending.IsCompleted.ShouldBeFalse();
        coroutine.Status.ShouldBe(LuauCoroutineStatus.Suspended);
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => pending.AsTask());
        coroutine.Status.ShouldBe(LuauCoroutineStatus.Finished);
        Should.Throw<InvalidOperationException>(() => coroutine.Resume());
    }

    [Fact]
    public async Task StateDisposed_DuringAwait_ShouldFaultThePendingInvocation()
    {
        var state = new LuauState();
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using LuauFunction wait = state.CreateFunctionBuilder(args => args.AwaitOrError(() => AddLater(gate.Task, 0)));
        state.Globals.Set("wait", wait);

        ValueTask pending = state.Load("wait()").ExecuteAsync([], TestToken);
        pending.IsCompleted.ShouldBeFalse();
        state.Dispose();
        gate.SetResult(1);

        await Should.ThrowAsync<ObjectDisposedException>(() => pending.AsTask());
    }

    [Fact]
    public async Task StateDisposed_DuringAwait_ShouldNotRunTheConversion()
    {
        var state = new LuauState();
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool converted = false;
        using LuauFunction wait = state.CreateFunctionBuilder(args =>
            args.TryGetAwaiter(out LuauAwaiter awaiter, out string? error)
                ? awaiter.Await(
                    new ValueTask<int>(gate.Task),
                    value =>
                    {
                        converted = true;
                        return LuauReturn.Ok(value);
                    }
                )
                : LuauReturn.Error(error)
        );
        state.Globals.Set("wait", wait);

        ValueTask pending = state.Load("wait()").ExecuteAsync([], TestToken);
        state.Dispose();
        gate.SetResult(1);

        await Should.ThrowAsync<ObjectDisposedException>(() => pending.AsTask());
        converted.ShouldBeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUseTheChunkEnvironmentAndReturnTypedValues()
    {
        using LuauFunction echo = SetAsyncGlobal("echo", (value, _) => YieldThen(LuauReturn.Ok(value)));
        using LuauTable environment = _state.CreateEnvironment();
        environment.Set("base", 40);

        (int sum, string? missing) = await _state
            .Load(
                """
                local value = base + echo(2)
                scoped = true
                return value
                """
            )
            .WithEnvironment(environment)
            .ExecuteAsync<int, string?>([], TestToken);

        sum.ShouldBe(42);
        missing.ShouldBeNull();
        environment.ContainsKey("scoped").ShouldBeTrue();
        _state.Globals.ContainsKey("scoped").ShouldBeFalse();
    }

    [Fact]
    public async Task InvokeAsync_WithFourArgumentsAndFourResults_ShouldRoundTrip()
    {
        using LuauFunction echo = SetAsyncGlobal("echo", (value, _) => YieldThen(LuauReturn.Ok(value)));
        using LuauFunction function = _state
            .Load(
                """
                return function(a, b, c, d)
                    return echo(a), b .. "!", not c, d * 2
                end
                """
            )
            .Execute<LuauFunction>();

        (int a, string b, bool c, double d) = await function.InvokeAsync<int, string, bool, double>(
            [1, "two", true, 2.5],
            TestToken
        );

        a.ShouldBe(1);
        b.ShouldBe("two!");
        c.ShouldBeFalse();
        d.ShouldBe(5);
    }

    [Fact]
    public async Task InvokeAsync_WithoutAwaitingCallbacks_ShouldCompleteSynchronously()
    {
        using LuauFunction add = _state.Load("return function(a, b) return a + b end").Execute<LuauFunction>();
        ulong baseline = _state.MemoryStatistics.ActiveRegistryReferences;

#pragma warning disable xUnit1051 // Exercises the params overload, which has no token.
        ValueTask<int> pending = add.InvokeAsync<int>(1, 2);
#pragma warning restore xUnit1051

        pending.IsCompletedSuccessfully.ShouldBeTrue();
        (await pending).ShouldBe(3);
        _state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(baseline);
    }

    [Fact]
    public async Task InvokeAsync_WhenTheScriptYields_ShouldThrowLuaException()
    {
        using LuauFunction yielding = _state.Load("coroutine.yield(1)").ToFunction();

        LuaException exception = await Should.ThrowAsync<LuaException>(() => yielding.InvokeAsync().AsTask());

        exception.Message.ShouldContain("attempt to yield from outside a coroutine");
    }

    [Fact]
    public async Task ResumeAsync_ShouldAwaitCallbacksUntilTheCoroutineYields()
    {
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using LuauFunction addLater = SetAsyncGlobal("add_later", (value, _) => AddLater(gate.Task, value));
        using LuauFunction body = _state
            .Load(
                """
                local value = ...
                local received = coroutine.yield(add_later(value))
                return received + 1
                """
            )
            .ToFunction();
        using LuauCoroutine coroutine = _state.CreateCoroutine(body);

        ValueTask<int> pending = coroutine.ResumeAsync<int>([40], TestToken);
        pending.IsCompleted.ShouldBeFalse();
        coroutine.Status.ShouldBe(LuauCoroutineStatus.Suspended);
        Should.Throw<InvalidOperationException>(() => coroutine.Resume()).Message.ShouldContain("awaiting");
        gate.SetResult(2);

        (await pending).ShouldBe(42);
        coroutine.Status.ShouldBe(LuauCoroutineStatus.Suspended);
        (await coroutine.ResumeAsync<int>([10], TestToken)).ShouldBe(11);
        coroutine.Status.ShouldBe(LuauCoroutineStatus.Finished);
    }

    [Fact]
    public async Task ResumeAsync_WhenTheHandleIsDisposedDuringAwait_ShouldStillFinish()
    {
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using LuauFunction addLater = SetAsyncGlobal("add_later", (value, _) => AddLater(gate.Task, value));
        using LuauFunction body = _state.Load("return add_later(3)").ToFunction();
        ulong baseline = _state.MemoryStatistics.ActiveRegistryReferences;
        LuauCoroutine coroutine = _state.CreateCoroutine(body);

        ValueTask<int> pending = coroutine.ResumeAsync<int>([], TestToken);
        pending.IsCompleted.ShouldBeFalse();
        coroutine.Status.ShouldBe(LuauCoroutineStatus.Suspended);
        coroutine.Dispose();
        CollectGarbage();
        gate.SetResult(2);

        (await pending).ShouldBe(5);
        _state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(baseline);
    }

    [Fact]
    public async Task SuspendedInvocation_ShouldSurviveAGarbageCollection()
    {
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using LuauFunction addLater = SetAsyncGlobal("add_later", (value, _) => AddLater(gate.Task, value));

        ValueTask<int> pending = _state
            .Load("local text = string.rep('x', 3) return add_later(#text)")
            .ExecuteAsync<int>([], TestToken);
        pending.IsCompleted.ShouldBeFalse();
        CollectGarbage();
        gate.SetResult(2);

        (await pending).ShouldBe(5);
    }

    [Fact]
    public void Resume_WhenACallbackAwaits_ShouldFailInsideTheCoroutine()
    {
        using LuauFunction wait = SetAsyncGlobal("wait", (_, _) => new ValueTask<LuauReturn>(_never.Task));
        using LuauFunction body = _state.Load("wait()").ToFunction();
        using LuauCoroutine coroutine = _state.CreateCoroutine(body);

        LuaException exception = Should.Throw<LuaException>(() => coroutine.Resume());

        exception.Message.ShouldContain(AwaitRejectedMessage);
        coroutine.Status.ShouldBe(LuauCoroutineStatus.Error);
    }

    [Fact]
    public async Task Await_FromAUserdataMethod_ShouldSuspendTheScriptAndResumeItWithTheResult()
    {
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        SetAwaitingUserdata("waiter", (value, _) => AddLater(gate.Task, value));

        ValueTask<int> pending = _state.Load("return waiter:wait(40)").ExecuteAsync<int>([], TestToken);

        pending.IsCompleted.ShouldBeFalse();
        gate.SetResult(2);
        (await pending).ShouldBe(42);
    }

    [Fact]
    public void Await_FromAUserdataMethodInSyncExecute_ShouldBeALuaErrorCatchableByPcall()
    {
        SetAwaitingUserdata("waiter", (_, _) => new ValueTask<LuauReturn>(_never.Task));

        (bool ok, string error) = _state
            .Load(
                """
                local ok, err = pcall(function() return waiter:wait() end)
                return ok, tostring(err)
                """
            )
            .Execute<bool, string>();

        ok.ShouldBeFalse();
        error.ShouldContain(AwaitRejectedMessage);
    }

    [Fact]
    public async Task FaultedWork_FromAUserdataMethod_ShouldBeCatchableByScriptPcall()
    {
        SetAwaitingUserdata(
            "waiter",
            async (_, _) =>
            {
                await Task.Yield();
                throw new InvalidOperationException("boom from async method");
            }
        );

        (bool ok, string error) = await _state
            .Load(
                """
                local ok, err = pcall(function() return waiter:wait() end)
                return ok, tostring(err)
                """
            )
            .ExecuteAsync<bool, string>([], TestToken);

        ok.ShouldBeFalse();
        error.ShouldContain("boom from async method");
    }

    [Fact]
    public async Task NotHandledResult_AfterAwait_ShouldNotLeakTheInternalSentinel()
    {
        SetAwaitingUserdata("waiter", (_, _) => YieldThen(LuauReturn.NotHandledError));

        LuaException exception = await Should.ThrowAsync<LuaException>(() =>
            _state.Load("waiter:wait()").ExecuteAsync([], TestToken).AsTask()
        );

        exception.Message.ShouldContain("the callback did not handle the call");
        exception.Message.ShouldNotContain(LuauReturn.NotHandled);
    }

    [Fact]
    public async Task Cancellation_ShouldReachAUserdataMethodAndCancelTheInvocation()
    {
        using var cts = new CancellationTokenSource();
        SetAwaitingUserdata("waiter", (_, cancellationToken) => WaitForCancellation(cancellationToken));

        ValueTask pending = _state.Load("waiter:wait()").ExecuteAsync([], cts.Token);
        pending.IsCompleted.ShouldBeFalse();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => pending.AsTask());
        _state.Load("return 1 + 1").Execute<int>().ShouldBe(2);
    }

    /// <summary> Registers a global userdata whose methods await <paramref name="work"/> with their first argument. </summary>
    private void SetAwaitingUserdata(string name, Func<double, CancellationToken, ValueTask<LuauReturn>> work) =>
        _state.Globals.Set(name, IntoLuau.FromUserdata(new AwaitingUserdata(work)));

    private sealed class AwaitingUserdata(Func<double, CancellationToken, ValueTask<LuauReturn>> work)
        : ILuauUserData<AwaitingUserdata>
    {
        private readonly Func<double, CancellationToken, ValueTask<LuauReturn>> _work = work;

        public static LuauReturnSingle OnIndex(
            AwaitingUserdata self,
            in LuauState state,
            in ReadOnlySpan<char> fieldName
        ) => LuauReturnSingle.NotHandled;

        public static LuauOutcome OnSetIndex(
            AwaitingUserdata self,
            LuauArgsSingle args,
            in ReadOnlySpan<char> fieldName
        ) => LuauOutcome.NotHandledError;

        public static LuauReturn OnMethodCall(
            AwaitingUserdata self,
            LuauArgs functionArgs,
            in ReadOnlySpan<char> methodName
        )
        {
            double value =
                functionArgs.ArgumentCount > 0 && functionArgs.TryReadNumber(1, out double number, out _) ? number : 0;
            if (!functionArgs.TryGetAwaiter(out LuauAwaiter awaiter, out string? error))
                return LuauReturn.Error(error);
            return awaiter.Await(self._work(value, functionArgs.CancellationToken));
        }
    }

    [Fact]
    public async Task GeneratedUserdataMethod_ReturningATask_ShouldSuspendTheScriptAndResumeItWithTheResult()
    {
        var vault = new GeneratedVault();
        _state.Globals.Set("vault", IntoLuau.FromUserdata(vault));

        ValueTask<(int, string)> pending = _state
            .Load("return vault:deposit(40)")
            .ExecuteAsync<int, string>([], TestToken);

        pending.IsCompleted.ShouldBeFalse();
        vault.Gate.SetResult(2);
        (await pending).ShouldBe((42, "deposited"));
    }

    [Fact]
    public async Task GeneratedUserdataMethod_ShouldReceiveTheCancellationTokenOfTheHostCall()
    {
        using var cts = new CancellationTokenSource();
        _state.Globals.Set("vault", IntoLuau.FromUserdata(new GeneratedVault()));

        ValueTask pending = _state.Load("vault:lock()").ExecuteAsync([], cts.Token);
        pending.IsCompleted.ShouldBeFalse();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => pending.AsTask());
    }

    [Fact]
    public void GeneratedUserdataMethod_UnderSyncExecute_ShouldFailWithoutCallingTheMethod()
    {
        var vault = new GeneratedVault();
        _state.Globals.Set("vault", IntoLuau.FromUserdata(vault));

        LuaException exception = Should.Throw<LuaException>(() => _state.Load("vault:deposit(40)").Execute());

        exception.Message.ShouldContain(AwaitRejectedMessage);
        vault.DepositCalls.ShouldBe(0);
    }

    [Fact]
    public async Task GeneratedModuleFunction_ReturningAValueTask_ShouldSuspendTheScript()
    {
        _state.RegisterModule(GeneratedClockModule.ModuleName, GeneratedClockModule.OnLoad);

        ValueTask<int> pending = _state
            .Load("return require('clock').after_yield(21)")
            .ExecuteAsync<int>([], TestToken);

        (await pending).ShouldBe(42);
    }

    [Fact]
    public async Task Await_WithAConversion_ShouldSuspendTheScriptAndResumeItWithTheConvertedResult()
    {
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using LuauFunction add = _state.CreateFunctionBuilder(args =>
            args.TryGetAwaiter(out LuauAwaiter awaiter, out string? error)
                ? awaiter.Await(new ValueTask<int>(gate.Task), static value => LuauReturn.Ok(value + 40, "added"))
                : LuauReturn.Error(error)
        );
        _state.Globals.Set("add", add);

        ValueTask<(int, string)> pending = _state.Load("return add()").ExecuteAsync<int, string>([], TestToken);

        pending.IsCompleted.ShouldBeFalse();
        gate.SetResult(2);
        (await pending).ShouldBe((42, "added"));
    }

    [Fact]
    public async Task Await_WithAConversionOfCompletedWork_ShouldNotSuspend()
    {
        using LuauFunction add = _state.CreateFunctionBuilder(args =>
            args.TryGetAwaiter(out LuauAwaiter awaiter, out string? error)
                ? awaiter.Await(new ValueTask<int>(2), static value => LuauReturn.Ok(value + 40))
                : LuauReturn.Error(error)
        );
        _state.Globals.Set("add", add);

        ValueTask<int> pending = _state.Load("return add()").ExecuteAsync<int>([], TestToken);

        pending.IsCompletedSuccessfully.ShouldBeTrue();
        (await pending).ShouldBe(42);
    }

    [Fact]
    public async Task Await_WithAThrowingConversion_ShouldBeALuaErrorCatchableByPcall()
    {
        using LuauFunction fail = _state.CreateFunctionBuilder(args =>
            args.TryGetAwaiter(out LuauAwaiter awaiter, out string? error)
                ? awaiter.Await(
                    new ValueTask<int>(Task.Run(static () => 1, TestToken)),
                    static _ => throw new InvalidOperationException("boom from conversion")
                )
                : LuauReturn.Error(error)
        );
        _state.Globals.Set("fail", fail);

        (bool ok, string error) = await _state
            .Load(
                """
                local ok, err = pcall(fail)
                return ok, tostring(err)
                """
            )
            .ExecuteAsync<bool, string>([], TestToken);

        ok.ShouldBeFalse();
        error.ShouldContain("boom from conversion");
    }

    [Fact]
    public async Task Await_OfWorkWithoutAResult_ShouldResumeTheScriptWithoutValues()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using LuauFunction wait = _state.CreateFunctionBuilder(args =>
            args.TryGetAwaiter(out LuauAwaiter awaiter, out string? error)
                ? awaiter.Await(new ValueTask(gate.Task))
                : LuauReturn.Error(error)
        );
        _state.Globals.Set("wait", wait);

        ValueTask<int> pending = _state.Load("return select('#', wait())").ExecuteAsync<int>([], TestToken);

        pending.IsCompleted.ShouldBeFalse();
        gate.SetResult();
        (await pending).ShouldBe(0);
    }

    [Fact]
    public async Task CreateFunction_WithAnAsyncDelegate_ShouldSuspendTheScriptAndResumeItWithTheResult()
    {
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using LuauFunction add = _state.CreateFunction(async (int value) => (value + await gate.Task, "added"));
        _state.Globals.Set("add", add);

        ValueTask<(int, string)> pending = _state.Load("return add(40)").ExecuteAsync<int, string>([], TestToken);

        pending.IsCompleted.ShouldBeFalse();
        gate.SetResult(2);
        (await pending).ShouldBe((42, "added"));
    }

    [Fact]
    public async Task CreateFunction_ShouldPassTheCancellationTokenOfTheHostCall()
    {
        using var cts = new CancellationTokenSource();
        using LuauFunction wait = _state.CreateFunction(
            (CancellationToken cancellationToken) => Task.Delay(Timeout.Infinite, cancellationToken)
        );
        _state.Globals.Set("wait", wait);

        ValueTask pending = _state.Load("wait()").ExecuteAsync([], cts.Token);
        pending.IsCompleted.ShouldBeFalse();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => pending.AsTask());
    }

    [Fact]
    public async Task ScriptResumingACoroutineThatAwaits_ShouldFailAndEndTheHostCall()
    {
        var work = new TaskCompletionSource<LuauReturn>(TaskCreationOptions.RunContinuationsAsynchronously);
        using LuauFunction slow = SetAsyncGlobal("slow", (_, _) => new ValueTask<LuauReturn>(work.Task));

        ValueTask pending = _state
            .Load(
                """
                waiting = coroutine.running()
                received = slow()
                continued = true
                """
            )
            .ExecuteAsync([], TestToken);
        pending.IsCompleted.ShouldBeFalse();

        // Only the host delivers the result of the callback. A script cannot forge it.
        _state.Load("ok, err = coroutine.resume(waiting, 'forged')").Execute();
        work.SetResult(LuauReturn.Ok(1));

        LuaException exception = await Should.ThrowAsync<LuaException>(() => pending.AsTask());
        exception.Message.ShouldContain("resumed or closed the coroutine");
        _state.Globals.TryGet("ok", out bool ok).ShouldBeTrue();
        ok.ShouldBeFalse();
        _state.Globals.TryGet("err", out string? error).ShouldBeTrue();
        error.ShouldContain("waiting for a managed callback");
        _state.Globals.ContainsKey("received").ShouldBeFalse();
        _state.Globals.ContainsKey("continued").ShouldBeFalse();
    }

    [Fact]
    public async Task ScriptResumingACoroutineThatAwaits_WhenItCatchesTheError_ShouldNotGetTheResultLater()
    {
        var work = new TaskCompletionSource<LuauReturn>(TaskCreationOptions.RunContinuationsAsynchronously);
        using LuauFunction slow = SetAsyncGlobal("slow", (_, _) => new ValueTask<LuauReturn>(work.Task));

        ValueTask pending = _state
            .Load(
                """
                waiting = coroutine.running()
                local _, first = pcall(slow)
                caught = first
                -- The host call no longer runs this coroutine, so it cannot await again.
                local _, second = pcall(slow)
                refused = second
                received = coroutine.yield("parked")
                """
            )
            .ExecuteAsync([], TestToken);
        pending.IsCompleted.ShouldBeFalse();

        _state.Load("ok, parked = coroutine.resume(waiting)").Execute();
        _startedWorkCount.ShouldBe(1);
        work.SetResult(LuauReturn.Ok("late"));

        LuaException exception = await Should.ThrowAsync<LuaException>(() => pending.AsTask());
        exception.Message.ShouldContain("resumed or closed the coroutine");
        _state.Globals.TryGet("caught", out string? caught).ShouldBeTrue();
        caught.ShouldContain("waiting for a managed callback");
        _state.Globals.TryGet("refused", out string? refused).ShouldBeTrue();
        refused.ShouldContain(AwaitRejectedMessage);
        _state.Globals.TryGet("parked", out string? parked).ShouldBeTrue();
        parked.ShouldBe("parked");
        // The coroutine still waits at its own yield. The result of the callback was not delivered there.
        _state.Globals.ContainsKey("received").ShouldBeFalse();
        _state.Load("return coroutine.status(waiting)").Execute<string>().ShouldBe("suspended");
    }

    [Fact]
    public async Task LostCoroutine_ThatAnotherHostCallRunsByNow_ShouldNotGetTheResultOfTheFirstCall()
    {
        var first = new TaskCompletionSource<LuauReturn>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<LuauReturn>(TaskCreationOptions.RunContinuationsAsynchronously);
        using LuauFunction slow = SetAsyncGlobal("slow", (_, _) => new ValueTask<LuauReturn>(first.Task));
        using LuauFunction slower = SetAsyncGlobal("slower", (_, _) => new ValueTask<LuauReturn>(second.Task));
        using LuauFunction body = _state
            .Load(
                """
                pcall(slow)
                coroutine.yield("parked")
                received = slower()
                """
            )
            .ToFunction();
        using LuauCoroutine coroutine = _state.CreateCoroutine(body);
        _state.Globals.Set("waiting", coroutine);

        // The first call loses the coroutine to a script, and a second call picks it up where the script left it.
        ValueTask firstCall = coroutine.ResumeAsync([], TestToken);
        _state.Load("coroutine.resume(waiting)").Execute();
        ValueTask secondCall = coroutine.ResumeAsync([], TestToken);
        secondCall.IsCompleted.ShouldBeFalse();

        first.SetResult(LuauReturn.Ok("from the first callback"));
        await Should.ThrowAsync<LuaException>(() => firstCall.AsTask());
        _state.Globals.ContainsKey("received").ShouldBeFalse();

        second.SetResult(LuauReturn.Ok("from the second callback"));
        await secondCall;
        _state.Globals.TryGet("received", out string? received).ShouldBeTrue();
        received.ShouldBe("from the second callback");
    }

    [Fact]
    public async Task ScriptClosingACoroutineThatAwaits_ShouldEndTheHostCall()
    {
        var work = new TaskCompletionSource<LuauReturn>(TaskCreationOptions.RunContinuationsAsynchronously);
        using LuauFunction slow = SetAsyncGlobal("slow", (_, _) => new ValueTask<LuauReturn>(work.Task));
        using LuauFunction body = _state.Load("received = slow()").ToFunction();
        using LuauCoroutine coroutine = _state.CreateCoroutine(body);
        _state.Globals.Set("waiting", coroutine);

        ValueTask pending = coroutine.ResumeAsync([], TestToken);
        pending.IsCompleted.ShouldBeFalse();

        _state.Load("closed = coroutine.close(waiting)").Execute();
        coroutine.Status.ShouldBe(LuauCoroutineStatus.Finished);
        work.SetResult(LuauReturn.Ok(1));

        LuaException exception = await Should.ThrowAsync<LuaException>(() => pending.AsTask());
        exception.Message.ShouldContain("resumed or closed the coroutine");
        _state.Globals.TryGet("closed", out bool closed).ShouldBeTrue();
        closed.ShouldBeTrue();
        _state.Globals.ContainsKey("received").ShouldBeFalse();
    }

    [Fact]
    public async Task InvocationThatYields_ShouldLeaveNoCoroutineToContinue()
    {
        LuaException exception = await Should.ThrowAsync<LuaException>(() =>
            _state
                .Load(
                    """
                    kept = coroutine.running()
                    coroutine.yield()
                    leaked = true
                    """
                )
                .ExecuteAsync([], TestToken)
                .AsTask()
        );
        exception.Message.ShouldContain("attempt to yield");

        // The call is over. A script that kept its coroutine cannot run the rest of it later.
        _state.Load("resumed = coroutine.resume(kept) status = coroutine.status(kept)").Execute();

        _state.Globals.TryGet("resumed", out bool resumed).ShouldBeTrue();
        resumed.ShouldBeFalse();
        _state.Globals.TryGet("status", out string? status).ShouldBeTrue();
        status.ShouldBe("dead");
        _state.Globals.ContainsKey("leaked").ShouldBeFalse();
    }

    public void Dispose() => _state.Dispose();
}

[LuauUserdata]
public sealed partial class GeneratedVault
{
    public TaskCompletionSource<int> Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int DepositCalls { get; private set; }

    [LuauMember("deposit")]
    public async Task<(int Balance, string Status)> DepositAsync(int amount)
    {
        DepositCalls++;
        return (amount + await Gate.Task, "deposited");
    }

    [LuauMember("lock")]
    public Task LockAsync(CancellationToken cancellationToken) => Task.Delay(Timeout.Infinite, cancellationToken);
}

[LuauModule("clock")]
public static partial class GeneratedClockModule
{
    [LuauMember("after_yield")]
    public static async ValueTask<int> AfterYieldAsync(int value)
    {
        await Task.Yield();
        return value * 2;
    }
}
