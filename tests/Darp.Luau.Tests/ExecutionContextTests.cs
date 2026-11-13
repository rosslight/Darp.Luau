using System.Collections.Concurrent;
using Darp.Luau.Internal;
using Shouldly;

namespace Darp.Luau.Tests;

/// <summary> Continuations of async managed callbacks run one turn at a time on the state's context. </summary>
public sealed class ExecutionContextTests : IDisposable
{
    private readonly LuauState _state = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Continuations_UsingTheStateRightAfterTaskYield_ShouldNotOverlapOtherTurns()
    {
        LuauState state = _state;
        using LuauFunction touch = _state.CreateFunctionManual(args =>
        {
            LuauFunctionView callbackView = args[1].Get<LuauFunctionView>();
            int value = args[2].Get<int>();
            LuauAwaiter awaiter = args.CreateAwaiter();
            return awaiter.Await(TouchAsync(state, callbackView.ToOwned(), value));

            static async ValueTask<LuauReturn> TouchAsync(LuauState state, LuauFunction callback, int value)
            {
                using (callback)
                {
                    await Task.Yield();
                    using LuauTable table = state.CreateTable();
                    table.Set("value", callback.Invoke<int>(value));
                    return LuauReturn.Ok(table);
                }
            }
        });
        _state.Globals.Set("touch", touch);

        int sum = await _state
            .Load(
                """
                local sum = 0
                for i = 1, 1000 do
                    sum = sum + touch(function(x) return x end, i).value
                end
                return sum
                """
            )
            .ExecuteAsync<int>([], TestToken);

        sum.ShouldBe(500500);
    }

    [Fact]
    public async Task ConcurrentInvocations_WithRandomDelays_ShouldAllReturnTheirResults()
    {
        LuauState state = _state;
        using LuauFunction delayed = _state.CreateFunctionManual(args =>
        {
            int value = args[1].Get<int>();
            return args.AwaitOrError(() => DelayedAsync(state, value));

            static async ValueTask<LuauReturn> DelayedAsync(LuauState state, int value)
            {
                await Task.Delay(Random.Shared.Next(0, 5));
                int doubled = state.Load("local x = ... return x * 2").Execute<int>(value);
                return LuauReturn.Ok(doubled);
            }
        });
        _state.Globals.Set("delayed", delayed);
        using LuauFunction function = _state
            .Load("return function(value) return delayed(value) + delayed(1) end")
            .Execute<LuauFunction>();

        Task<int>[] invocations = Enumerable
            .Range(0, 50)
            .Select(i => function.InvokeAsync<int>([i], TestToken).AsTask())
            .ToArray();
        int[] results = await Task.WhenAll(invocations);

        results.ShouldBe(Enumerable.Range(0, 50).Select(i => (i * 2) + 2));
    }

    [Fact]
    public async Task Start_WhileAnotherThreadExecutesATurn_ShouldBeQueuedWithItsArguments()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using LuauFunction block = _state.CreateFunctionManual(args =>
            args.AwaitOrError(() => BlockAsync(entered, release))
        );
        _state.Globals.Set("block", block);
        using LuauFunction describe = _state
            .Load("return function(name, count, table) return name .. count .. table.suffix end")
            .Execute<LuauFunction>();
        using LuauTable table = _state.CreateTable();
        table.Set("suffix", "!");

        ValueTask blocking = _state.Load("block()").ExecuteAsync([], TestToken);
        entered.Wait(TestToken);
        ValueTask<string> queued = describe.InvokeAsync<string>(["run", 2, table], TestToken);
        queued.IsCompleted.ShouldBeFalse();
        release.Set();

        await blocking;
        (await queued).ShouldBe("run2!");
        return;

        static async ValueTask<LuauReturn> BlockAsync(ManualResetEventSlim entered, ManualResetEventSlim release)
        {
            await Task.Yield();
            // This continuation owns the state while it blocks.
            entered.Set();
            release.Wait();
            return LuauReturn.Ok();
        }
    }

    [Fact]
    public async Task HostDispatcher_ShouldRunCallsStartedElsewhereAndAllContinuationsOnItsThread()
    {
        using var dispatcher = new SingleThreadSynchronizationContext();
        using var state = new LuauState(LuauLibraries.All, null, dispatcher);
        var callbackThreads = new ConcurrentBag<int>();
        var continuationThreads = new ConcurrentBag<int>();
        using LuauFunction record = state.CreateFunctionManual(args =>
        {
            callbackThreads.Add(Environment.CurrentManagedThreadId);
            return args.AwaitOrError(() => RecordAsync(continuationThreads));
        });
        state.Globals.Set("record", record);

        // Started off the dispatcher: posted to it.
        Environment.CurrentManagedThreadId.ShouldNotBe(dispatcher.ThreadId);
        await state.Load("record() record()").ExecuteAsync([], TestToken);
        // Started on the dispatcher: admitted inline, and completes synchronously without awaiting callbacks.
        await dispatcher.Run(async () =>
        {
            ValueTask<int> completed = state.Load("return 1").ExecuteAsync<int>([], TestToken);
            completed.IsCompletedSuccessfully.ShouldBeTrue();
            await state.Load("record()").ExecuteAsync([], TestToken);
        });

        callbackThreads.Count.ShouldBe(3);
        callbackThreads.ShouldAllBe(threadId => threadId == dispatcher.ThreadId);
        continuationThreads.Count.ShouldBe(6);
        continuationThreads.ShouldAllBe(threadId => threadId == dispatcher.ThreadId);
        return;

        static async ValueTask<LuauReturn> RecordAsync(ConcurrentBag<int> threads)
        {
            await Task.Yield();
            threads.Add(Environment.CurrentManagedThreadId);
            await Task.Delay(1);
            threads.Add(Environment.CurrentManagedThreadId);
            return LuauReturn.Ok();
        }
    }

    [Fact]
    public async Task NestedCalls_FromCallbacksAndTheirContinuations_ShouldWork()
    {
        using LuauFunction echo = _state.CreateFunctionManual(args =>
        {
            int value = args[1].Get<int>();
            return args.AwaitOrError(() => EchoAsync(value));
        });
        _state.Globals.Set("echo", echo);
        using LuauFunction inner = _state.Load("return function(x) return echo(x) + 1 end").Execute<LuauFunction>();
        using LuauFunction outer = _state.CreateFunctionManual(args => args.AwaitOrError(() => OuterAsync(inner)));
        _state.Globals.Set("outer", outer);
        using LuauFunction add = _state.Load("return function(a, b) return a + b end").Execute<LuauFunction>();
        using LuauFunction syncInvoke = _state.CreateFunctionManual(_ => LuauReturn.Ok(add.Invoke<int>(20, 1)));
        _state.Globals.Set("sync_invoke", syncInvoke);

        (int nested, int sync) = await _state
            .Load("return outer(), sync_invoke()")
            .ExecuteAsync<int, int>([], TestToken);

        nested.ShouldBe(42);
        sync.ShouldBe(21);
        return;

        static async ValueTask<LuauReturn> EchoAsync(int value)
        {
            await Task.Yield();
            return LuauReturn.Ok(value);
        }

        static async ValueTask<LuauReturn> OuterAsync(LuauFunction inner)
        {
            await Task.Yield();
            int result = await inner.InvokeAsync<int>([40], TestToken);
            return LuauReturn.Ok(result + 1);
        }
    }

    [Fact]
    public async Task SyncCalls_WhileAnotherThreadExecutesATurn_ShouldThrowAndLeaveTheStateAsItIs()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using LuauFunction block = _state.CreateFunctionManual(_ =>
        {
            entered.Set();
            release.Wait();
            return LuauReturn.Ok();
        });
        _state.Globals.Set("block", block);
        using LuauFunction add = _state.Load("return function(a, b) return a + b end").Execute<LuauFunction>();
        using LuauCoroutine coroutine = _state.CreateCoroutine(add);
        LuauTable globals = _state.Globals;

        Task<int> running = Task.Run(() => _state.Load("block() return 1").Execute<int>(), TestToken);
        entered.Wait(TestToken);

        Should
            .Throw<InvalidOperationException>(() => add.Invoke<int>(1, 2))
            .Message.ShouldContain("in use by another thread");
        Should.Throw<InvalidOperationException>(() => _state.Load("return 1").Execute<int>());
        Should.Throw<InvalidOperationException>(() => coroutine.Resume<int>(1, 2));
        Should.Throw<InvalidOperationException>(() => globals.Get<LuauFunction>("block"));
        Should.Throw<InvalidOperationException>(() => globals.Set("refused", true));
        Should.Throw<InvalidOperationException>(() => _state.Dispose());
        release.Set();

        (await running).ShouldBe(1);
        _state.IsDisposed.ShouldBeFalse();
        globals.ContainsKey("refused").ShouldBeFalse();
        add.Invoke<int>(1, 2).ShouldBe(3);
        coroutine.Resume<int>(1, 2).ShouldBe(3);
    }

    [Fact]
    public void TableAccess_ThatFails_ShouldGiveTheTurnBack()
    {
        using var otherState = new LuauState();
        using LuauTable foreign = otherState.CreateTable();
        using LuauTable guarded = _state
            .Load(
                """
                local guarded = setmetatable({}, { __index = function() error("no reading") end })
                return table.freeze(guarded)
                """
            )
            .Execute<LuauTable>();

        // A key or a value that cannot be pushed, and an error that Luau raises.
        Should.Throw<InvalidOperationException>(() => guarded.Get<int>(foreign));
        Should.Throw<InvalidOperationException>(() => guarded.Set(foreign, 1));
        Should.Throw<InvalidOperationException>(() => guarded.Set("key", foreign));
        Should.Throw<LuaException>(() => guarded.Set("key", 1));
        Should.Throw<LuaGetException>(() => guarded.Get<int>("key"));
        Should.Throw<LuaException>(() => guarded.ContainsKey("key"));
        guarded.TryGet("key", out int _).ShouldBeFalse();

        // Each access ended its host call, so another thread gets the state.
        LuauState state = _state;
        Exception? failure = null;
        var otherThread = new Thread(() => failure = Record.Exception(() => state.Globals.Set("free", true)));
        otherThread.Start();
        otherThread.Join();
        failure.ShouldBeNull();
    }

    [Fact]
    public async Task AsyncCall_WhileAnotherThreadRunsASyncCall_ShouldBeQueuedBehindIt()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using LuauFunction block = _state.CreateFunctionManual(_ =>
        {
            entered.Set();
            release.Wait();
            return LuauReturn.Ok();
        });
        _state.Globals.Set("block", block);

        Task running = Task.Run(() => _state.Load("block() finished = true").Execute(), TestToken);
        entered.Wait(TestToken);
        ValueTask<bool> queued = _state.Load("return finished").ExecuteAsync<bool>([], TestToken);
        queued.IsCompleted.ShouldBeFalse();
        release.Set();

        await running;
        (await queued).ShouldBeTrue();
    }

    [Fact]
    public async Task SyncCall_RightAfterAnAsyncCallCompleted_ShouldFindTheStateFree()
    {
        using LuauFunction yield = _state.CreateFunctionManual(args =>
            args.AwaitOrError(async () =>
            {
                await Task.Yield();
                return LuauReturn.Ok();
            })
        );
        _state.Globals.Set("yield", yield);
        using LuauFunction yielding = _state.Load("yield()").ToFunction();

        // Each call finishes in a turn on a thread-pool thread. Its caller hears of it when that turn has ended.
        for (int i = 0; i < 500; i++)
        {
            await yielding.InvokeAsync([], TestToken);
            _state.Globals.Set("count", i);
        }

        _state.Globals.Get<int>("count").ShouldBe(499);
    }

    [Fact]
    public async Task AsyncCall_ThatFinishesInsideTheTurnOfAnotherCall_ShouldCompleteWhenThatTurnHasEnded()
    {
        // Its continuations run inline: the waiting call goes on inside the callback that completes the gate.
        var gate = new TaskCompletionSource<int>();
        using LuauFunction wait = _state.CreateFunctionManual(args => args.AwaitOrError(() => WaitAsync(gate.Task)));
        _state.Globals.Set("wait", wait);
        using LuauFunction finish = _state.CreateFunctionManual(_ =>
        {
            gate.SetResult(42);
            // The waiting call has finished, but this turn has not.
            Thread.Sleep(50);
            return LuauReturn.Ok();
        });
        _state.Globals.Set("finish", finish);
        LuauState state = _state;

        ValueTask<int> waiting = _state.Load("return wait()").ExecuteAsync<int>([], TestToken);
        Task<int> observed = UseTheStateAfter(waiting, state);
        await _state.Load("finish()").ExecuteAsync([], TestToken);

        (await observed).ShouldBe(42);
        return;

        static async ValueTask<LuauReturn> WaitAsync(Task<int> gate) => LuauReturn.Ok(await gate);

        static async Task<int> UseTheStateAfter(ValueTask<int> call, LuauState state)
        {
            int result = await call;
            state.Globals.Set("result", result);
            return state.Globals.Get<int>("result");
        }
    }

    [Fact]
    public async Task AsyncCall_StartedByACallbackOfASyncCall_ShouldContinueWhenTheSyncCallHasReturned()
    {
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using LuauFunction wait = _state.CreateFunctionManual(args => args.AwaitOrError(() => WaitAsync(gate.Task)));
        _state.Globals.Set("wait", wait);
        using LuauFunction waiting = _state.Load("return wait(), returned").ToFunction();
        ValueTask<(int, bool)> started = default;
        using LuauFunction start = _state.CreateFunctionManual(_ =>
        {
            started = waiting.InvokeAsync<int, bool>([], TestToken);
            // The continuation is queued now. The sync call is a turn, so no other thread runs it meanwhile.
            gate.SetResult(42);
            Thread.Sleep(20);
            return LuauReturn.Ok();
        });
        _state.Globals.Set("start", start);

        _state.Load("start() returned = true").Execute();

        (await started).ShouldBe((42, true));
        return;

        static async ValueTask<LuauReturn> WaitAsync(Task<int> gate) => LuauReturn.Ok(await gate);
    }

    [Fact]
    public async Task SyncCall_OnTheDispatcherThread_ShouldRunAheadOfQueuedWork()
    {
        using var dispatcher = new SingleThreadSynchronizationContext();
        using var state = new LuauState(LuauLibraries.All, null, dispatcher);
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using LuauFunction wait = state.CreateFunctionManual(args => args.AwaitOrError(() => WaitAsync(gate.Task)));
        state.Globals.Set("wait", wait);

        await dispatcher.Run(async () =>
        {
            ValueTask<int> pending = state.Load("return wait() + offset").ExecuteAsync<int>([], TestToken);
            // The continuation is queued for the dispatcher, which is busy with this code.
            gate.SetResult(40);
            state.Globals.Set("offset", 2);

            (await pending).ShouldBe(42);
        });
        return;

        static async ValueTask<LuauReturn> WaitAsync(Task<int> gate) => LuauReturn.Ok(await gate);
    }

    [Fact]
    public async Task QueuedResume_OfACoroutineThatFinishedMeanwhile_ShouldFailItsCall()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using LuauFunction block = _state.CreateFunctionManual(_ =>
        {
            entered.Set();
            release.Wait();
            return LuauReturn.Ok();
        });
        _state.Globals.Set("block", block);
        using LuauFunction body = _state.Load("return 1").ToFunction();
        using LuauCoroutine coroutine = _state.CreateCoroutine(body);

        Task running = Task.Run(() => _state.Load("block()").Execute(), TestToken);
        entered.Wait(TestToken);
        ValueTask<int> first = coroutine.ResumeAsync<int>([], TestToken);
        ValueTask<int> second = coroutine.ResumeAsync<int>([], TestToken);
        release.Set();

        await running;
        (await first).ShouldBe(1);
        InvalidOperationException exception = await Should.ThrowAsync<InvalidOperationException>(() => second.AsTask());
        exception.Message.ShouldContain("Finished");
    }

    [Fact]
    public async Task StateDisposed_BeforeCompletedWorkWasDelivered_ShouldFailTheCallWithoutConverting()
    {
        using var dispatcher = new SingleThreadSynchronizationContext();
        var state = new LuauState(LuauLibraries.All, null, dispatcher);
        // Its continuations run inline, so the result is on its way when SetResult returns.
        var gate = new TaskCompletionSource<int>();
        bool converted = false;
        using LuauFunction wait = state.CreateFunctionManual(args =>
            args.CreateAwaiter()
                .Await(
                    new ValueTask<int>(gate.Task),
                    value =>
                    {
                        converted = true;
                        return LuauReturn.Ok(value);
                    }
                )
        );
        state.Globals.Set("wait", wait);

        await dispatcher.Run(async () =>
        {
            ValueTask pending = state.Load("wait()").ExecuteAsync([], TestToken);
            // The work is done, but the dispatcher is busy with this code and has not delivered its result.
            gate.SetResult(1);
            state.Dispose();

            await Should.ThrowAsync<ObjectDisposedException>(() => pending.AsTask());
        });

        converted.ShouldBeFalse();
    }

    [Fact]
    public async Task HostCalls_ShouldRestoreTheCallersContextAndNotLeakTheirOwn()
    {
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using LuauFunction wait = _state.CreateFunctionManual(args => args.AwaitOrError(() => WaitAsync(gate.Task)));
        _state.Globals.Set("wait", wait);
        SynchronizationContext? before = SynchronizationContext.Current;

        ValueTask<int> completed = _state.Load("return 1").ExecuteAsync<int>([], TestToken);
        SynchronizationContext.Current.ShouldBe(before);
        ValueTask<int> suspended = _state.Load("return wait()").ExecuteAsync<int>([], TestToken);
        SynchronizationContext.Current.ShouldBe(before);
        completed.IsCompletedSuccessfully.ShouldBeTrue();
        suspended.IsCompleted.ShouldBeFalse();
        gate.SetResult(2);

        (await suspended).ShouldBe(2);
        SynchronizationContext.Current.ShouldNotBeOfType<LuauSynchronizationContext>();
        return;

        static async ValueTask<LuauReturn> WaitAsync(Task<int> gate) => LuauReturn.Ok(await gate);
    }

    public void Dispose() => _state.Dispose();

    /// <summary> A host dispatcher that runs everything posted to it on one dedicated thread. </summary>
    [Fact]
    public async Task HostDispatcher_ThatRefusesWork_ShouldFailTheCallAndLeaveTheStateUsable()
    {
        var dispatcher = new RefusingSynchronizationContext { Refuse = true };
        using var state = new LuauState(LuauLibraries.All, null, dispatcher);

        // Started off the dispatcher, so the call is posted to it.
        InvalidOperationException exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            state.Load("ran = true return 1").ExecuteAsync<int>([], TestToken).AsTask()
        );
        exception.Message.ShouldContain("shut down");

        dispatcher.Refuse = false;
        (await state.Load("return 2").ExecuteAsync<int>([], TestToken)).ShouldBe(2);
        // The refused call was reported as failed, so it does not run later.
        (await state.Load("return ran == nil").ExecuteAsync<bool>([], TestToken)).ShouldBeTrue();
    }

    /// <summary> Runs posted work on the thread pool, one item at a time, unless it refuses to. </summary>
    private sealed class RefusingSynchronizationContext : SynchronizationContext
    {
        private readonly SemaphoreSlim _turn = new(1, 1);

        public bool Refuse { get; set; }

        public override void Post(SendOrPostCallback d, object? state)
        {
            if (Refuse)
                throw new InvalidOperationException("the dispatcher has shut down");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                _turn.Wait();
                try
                {
                    SetSynchronizationContext(this);
                    d(state);
                }
                finally
                {
                    SetSynchronizationContext(null);
                    _turn.Release();
                }
            });
        }
    }

    private sealed class SingleThreadSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];
        private readonly Thread _thread;

        public SingleThreadSynchronizationContext()
        {
            _thread = new Thread(Pump) { IsBackground = true, Name = "test dispatcher" };
            _thread.Start();
        }

        public int ThreadId => _thread.ManagedThreadId;

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public Task Run(Func<Task> action)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(
                async _ =>
                {
                    try
                    {
                        await action();
                        completion.SetResult();
                    }
                    catch (Exception exception)
                    {
                        completion.SetException(exception);
                    }
                },
                null
            );
            return completion.Task;
        }

        private void Pump()
        {
            SetSynchronizationContext(this);
            foreach ((SendOrPostCallback callback, object? state) in _queue.GetConsumingEnumerable())
                callback(state);
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            _thread.Join();
            _queue.Dispose();
        }
    }
}
