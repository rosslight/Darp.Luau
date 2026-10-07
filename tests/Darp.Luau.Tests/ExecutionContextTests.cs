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
        using LuauFunction touch = _state.CreateFunctionBuilder(args =>
        {
            if (!args.TryReadLuauFunction(1, out LuauFunctionView callbackView, out string? error))
                return LuauReturn.Error(error);
            if (!args.TryReadNumber(2, out int value, out error))
                return LuauReturn.Error(error);
            return LuauReturn.Await(TouchAsync(state, callbackView.ToOwned(), value));

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
        using LuauFunction delayed = _state.CreateFunctionBuilder(args =>
        {
            if (!args.TryReadNumber(1, out int value, out string? error))
                return LuauReturn.Error(error);
            return LuauReturn.Await(DelayedAsync(state, value));

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
        using LuauFunction block = _state.CreateFunctionBuilder(_ => LuauReturn.Await(BlockAsync(entered, release)));
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
        using LuauFunction record = state.CreateFunctionBuilder(_ =>
        {
            callbackThreads.Add(Environment.CurrentManagedThreadId);
            return LuauReturn.Await(RecordAsync(continuationThreads));
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
        using LuauFunction echo = _state.CreateFunctionBuilder(args =>
            args.TryReadNumber(1, out int value, out string? error)
                ? LuauReturn.Await(EchoAsync(value))
                : LuauReturn.Error(error)
        );
        _state.Globals.Set("echo", echo);
        using LuauFunction inner = _state.Load("return function(x) return echo(x) + 1 end").Execute<LuauFunction>();
        using LuauFunction outer = _state.CreateFunctionBuilder(_ => LuauReturn.Await(OuterAsync(inner)));
        _state.Globals.Set("outer", outer);
        using LuauFunction add = _state.Load("return function(a, b) return a + b end").Execute<LuauFunction>();
        using LuauFunction syncInvoke = _state.CreateFunctionBuilder(_ => LuauReturn.Ok(add.Invoke<int>(20, 1)));
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
    public async Task HostCalls_ShouldRestoreTheCallersContextAndNotLeakTheirOwn()
    {
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using LuauFunction wait = _state.CreateFunctionBuilder(_ => LuauReturn.Await(WaitAsync(gate.Task)));
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
