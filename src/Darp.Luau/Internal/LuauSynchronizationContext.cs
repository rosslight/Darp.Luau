using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Darp.Luau.Internal;

/// <summary>
/// Gives one <see cref="LuauState"/> to one thread at a time. The stretch for which a thread has it is a turn.
/// </summary>
/// <remarks>
/// <para>
/// Every host call runs in a turn. A sync call takes the state for as long as it runs, or fails when another
/// thread executes a turn (<see cref="EnterOrThrow"/>). An async call takes the state when it is free
/// (<see cref="TryEnter"/>) and is queued otherwise. Every continuation that captured this context is posted to
/// the queue and runs in a later turn, so code after an <c>await</c> in a managed callback never runs while
/// another turn executes Luau.
/// </para>
/// <para>
/// Queued work runs in a drain: on a thread-pool worker, or on the host dispatcher when one was given. A drain
/// that is scheduled reserves the state, so that an async call does not start ahead of what is queued.
/// </para>
/// <para>
/// This class is the only place in the library that synchronizes threads. They meet in the owner word and in the
/// queue, and nowhere else: the code of a turn runs without a lock.
/// </para>
/// </remarks>
internal sealed class LuauSynchronizationContext : SynchronizationContext
{
    private const int Idle = 0;

    // A drain is scheduled and has not started. No managed thread has this id.
    private const int Reserved = -1;

    private readonly SynchronizationContext? _hostContext;

    // Who has the state: Idle, Reserved, or the id of the managed thread that executes the turn. A thread takes
    // the state with a compare-exchange and gives it back with an exchange.
    private int _owner;

    // The work for later turns. Created by the first post, so that a state whose callbacks never await does not
    // pay for it.
    private ConcurrentQueue<(SendOrPostCallback Callback, object? State)>? _queue;

    // The async host calls that finished in the current turn. Only the owner of the turn uses the list.
    private IDetachedCall? _finishedCalls;

    public LuauSynchronizationContext(SynchronizationContext? hostContext) => _hostContext = hostContext;

    /// <summary> Whether the calling thread executes a turn, for assertions. </summary>
    public bool IsEntered => _owner == Environment.CurrentManagedThreadId;

    /// <summary>
    /// Enters a turn for an async host call, unless the state is taken or, with a host dispatcher, the calling
    /// thread is not the dispatcher's.
    /// </summary>
    /// <returns>
    /// The turn to dispose when the synchronous part of the work is done. When it was not entered, the work must
    /// be queued with <see cref="Queue{T}"/>.
    /// </returns>
    // Not inlined, and neither is the end of the turn: an async call site is large, and with these two inlined
    // into it the JIT inlines less of what the call spends its time in. Measured on InvokeAsync, 60 ns of 540.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public Turn TryEnter()
    {
        int currentThreadId = Environment.CurrentManagedThreadId;
        if (_owner == currentThreadId)
            return new Turn(this, isRoot: false);

        // With a host dispatcher, every turn of an async call runs on it. Dispatchers install themselves on their
        // thread.
        if (_hostContext is not null && !ReferenceEquals(Current, _hostContext))
            return default;

        return Interlocked.CompareExchange(ref _owner, currentThreadId, Idle) == Idle
            ? new Turn(this, isRoot: true)
            : default;
    }

    /// <summary> Enters a turn for a sync host call, unless the calling thread executes one already. </summary>
    /// <returns>Whether the call began a turn, which it ends with <see cref="EndTurn"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when another thread executes a turn.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool EnterOrThrow()
    {
        int currentThreadId = Environment.CurrentManagedThreadId;
        if (_owner == currentThreadId)
            return false;
        if (Interlocked.CompareExchange(ref _owner, currentThreadId, Idle) != Idle)
            TakeReservationOrThrow(currentThreadId);
        return true;
    }

    /// <summary> Ends the turn of the calling thread. </summary>
    /// <remarks>
    /// The async host calls that finished in the turn complete now and not earlier: their callers may use the
    /// state as soon as they hear of it. Work that was queued during the turn is handed to a drain.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EndTurn()
    {
        Debug.Assert(IsEntered);
        if (_finishedCalls is not null)
        {
            EndTurnAndCompleteCalls();
            return;
        }
        // A full fence. A post that does not find the state idle has queued its work before this exchange, so it
        // is seen below.
        Interlocked.Exchange(ref _owner, Idle);
        if (Volatile.Read(ref _queue) is not null)
            ScheduleQueuedWork();
    }

    /// <summary> Queues the start of async work that could not enter a turn. </summary>
    /// <param name="start">Starts the work inside a later turn.</param>
    /// <typeparam name="T">The result type of the work.</typeparam>
    /// <returns>A task that completes with the work.</returns>
    public ValueTask<T> Queue<T>(Func<ValueTask<T>> start)
    {
        var queuedStart = new QueuedStart<T>(this, start);
        try
        {
            Post(static queued => (queued as QueuedStart<T>)?.Run(), queuedStart);
        }
        catch (Exception exception)
        {
            // The host dispatcher refused the drain. The start stays queued; unless another turn ran it in the
            // meantime, it never runs and its caller is told that it failed.
            if (queuedStart.TryAbandon())
                return ValueTask.FromException<T>(exception);
        }
        return new ValueTask<T>(queuedStart.Task);
    }

    /// <summary>
    /// Returns <paramref name="operation"/> so that it completes when the turn it finished in has ended, and
    /// does not run the caller's code inside a turn.
    /// </summary>
    /// <remarks> Called inside the turn that started the operation. Only allocates when it is still running. </remarks>
    public ValueTask<T> Detach<T>(ValueTask<T> operation)
    {
        if (operation.IsCompleted)
            return operation;
        var call = new DetachedCall<T>(this);
        call.Await(operation);
        return new ValueTask<T>(call.Task);
    }

    /// <summary> Queues <paramref name="d"/> to run in a later turn. Never runs it on the calling stack. </summary>
    public override void Post(SendOrPostCallback d, object? state)
    {
        LazyInitializer
            .EnsureInitialized(ref _queue, static () => new ConcurrentQueue<(SendOrPostCallback, object?)>())
            .Enqueue((d, state));
        // Whoever has the state hands the queue to a drain when its turn ends.
        ScheduleQueuedWork();
    }

    /// <summary> Not supported: a turn must not block on another turn. </summary>
    public override void Send(SendOrPostCallback d, object? state) =>
        throw new NotSupportedException("A LuauState does not support synchronous dispatch.");

    /// <summary> Returns this context: copies must share the queue to serialize. </summary>
    public override SynchronizationContext CreateCopy() => this;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void TakeReservationOrThrow(int currentThreadId)
    {
        while (true)
        {
            int owner = Volatile.Read(ref _owner);
            // A drain that is scheduled has not started. The sync call goes first and schedules it again when it
            // ends, so a call on the thread of a host dispatcher never fails because of what is queued.
            if (owner is not (Idle or Reserved))
            {
                throw new InvalidOperationException(
                    "The LuauState is in use by another thread, which runs a script or the continuation of an "
                        + "async callback. A synchronous call does not wait for it: make the call from a "
                        + "callback, or when the async calls of the state have completed."
                );
            }
            if (Interlocked.CompareExchange(ref _owner, currentThreadId, owner) == owner)
                return;
        }
    }

    private void AddFinishedCall(IDetachedCall call)
    {
        Debug.Assert(IsEntered, "An async host call finishes in a turn.");
        call.Next = _finishedCalls;
        _finishedCalls = call;
    }

    /// <summary> <see cref="EndTurn"/> for the turn of an async call, see <see cref="TryEnter"/>. </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void EndRootTurn() => EndTurn();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void EndTurnAndCompleteCalls()
    {
        IDetachedCall? finishedCalls = _finishedCalls;
        _finishedCalls = null;
        Interlocked.Exchange(ref _owner, Idle);
        for (; finishedCalls is not null; finishedCalls = finishedCalls.Next)
            finishedCalls.Complete();
        ScheduleQueuedWork();
    }

    /// <summary> Schedules a drain when work is queued and nobody has the state. </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ScheduleQueuedWork()
    {
        // Reserved before the drain is published, so that no async call starts ahead of what is queued.
        if (
            Volatile.Read(ref _queue) is not { IsEmpty: false }
            || Interlocked.CompareExchange(ref _owner, Reserved, Idle) != Idle
        )
        {
            return;
        }
        if (_hostContext is null)
        {
            ThreadPool.UnsafeQueueUserWorkItem(static context => context.Drain(), this, preferLocal: false);
            return;
        }

        try
        {
            _hostContext.Post(static context => (context as LuauSynchronizationContext)?.Drain(), this);
        }
        catch
        {
            // No drain will run, for example because the dispatcher has shut down. Give the reservation back,
            // unless a sync call took it, so that the state stays usable: the next turn hands on what is queued.
            Interlocked.CompareExchange(ref _owner, Idle, Reserved);
            throw;
        }
    }

    private void Drain()
    {
        int currentThreadId = Environment.CurrentManagedThreadId;
        // A sync call may have taken the reservation. It schedules a drain again when it ends.
        if (Interlocked.CompareExchange(ref _owner, currentThreadId, Reserved) != Reserved)
            return;

        SynchronizationContext? previousContext = Current;
        SetSynchronizationContext(this);
        try
        {
            // A host call that finished completes when its turn has ended, so the turn ends with it. The rest of
            // the queue runs in a new drain.
            while (
                _finishedCalls is null
                && Volatile.Read(ref _queue) is { } queue
                && queue.TryDequeue(out (SendOrPostCallback Callback, object? State) work)
            )
            {
                work.Callback(work.State);
            }
        }
        finally
        {
            SetSynchronizationContext(previousContext);
            // An item that threw ends the drain early. The rest of the queue runs in a new one then, too.
            EndTurn();
        }
    }

    /// <summary> A turn entered by <see cref="TryEnter"/>. Installs the context on the calling thread. </summary>
    internal readonly ref struct Turn : IDisposable
    {
        private readonly LuauSynchronizationContext? _context;
        private readonly SynchronizationContext? _previousContext;
        private readonly bool _isRoot;

        /// <summary> Whether the calling thread is in the turn. A turn that was not entered ends nothing. </summary>
        public bool IsEntered => _context is not null;

        public Turn(LuauSynchronizationContext context, bool isRoot)
        {
            _context = context;
            _isRoot = isRoot;
            _previousContext = Current;
            SetSynchronizationContext(context);
        }

        public void Dispose()
        {
            if (_context is null)
                return;
            SetSynchronizationContext(_previousContext);
            if (_isRoot)
                _context.EndRootTurn();
        }
    }

    /// <summary> An async host call that finished in a turn and completes when that turn has ended. </summary>
    private interface IDetachedCall
    {
        /// <summary> Links the calls that finished in the same turn. </summary>
        IDetachedCall? Next { get; set; }

        void Complete();
    }

    /// <summary> What the caller of an async host call awaits when the call did not finish in its first turn. </summary>
    private sealed class DetachedCall<T>(LuauSynchronizationContext context)
        : TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously),
            IDetachedCall
    {
        private readonly LuauSynchronizationContext _context = context;
        private ValueTaskAwaiter<T> _operation;

        public IDetachedCall? Next { get; set; }

        /// <summary> Completes this call with <paramref name="operation"/>, once the turn it finishes in has ended. </summary>
        public void Await(ValueTask<T> operation)
        {
            // The awaiter captures the context of the state, which every turn of an async call installs. So the
            // operation is observed in a turn, which can then end before the caller is told.
            Debug.Assert(ReferenceEquals(Current, _context));
            _operation = operation.GetAwaiter();
            if (_operation.IsCompleted)
                Finish();
            else
                _operation.UnsafeOnCompleted(Finish);
        }

        public void Complete()
        {
            try
            {
                SetResult(_operation.GetResult());
            }
            catch (OperationCanceledException canceled)
            {
                SetCanceled(canceled.CancellationToken);
            }
            catch (Exception exception)
            {
                SetException(exception);
            }
        }

        private void Finish() => _context.AddFinishedCall(this);
    }

    private sealed class QueuedStart<T>(LuauSynchronizationContext context, Func<ValueTask<T>> start)
    {
        private readonly Func<ValueTask<T>> _start = start;
        private readonly DetachedCall<T> _call = new(context);

        // The caller's ambient data (AsyncLocal) flows into the queued start, like into any continuation.
        private readonly ExecutionContext? _executionContext = ExecutionContext.Capture();

        // 0 while queued. Running and abandoning both claim it, on different threads, so exactly one of them wins.
        private int _isClaimed;

        public Task<T> Task => _call.Task;

        /// <summary> Keeps the start from running, so that its caller can be told that it failed. </summary>
        /// <returns><c>false</c> when it runs or ran already.</returns>
        public bool TryAbandon() => Interlocked.Exchange(ref _isClaimed, 1) == 0;

        public void Run()
        {
            if (Interlocked.Exchange(ref _isClaimed, 1) != 0)
                return;
            if (_executionContext is null)
                Start();
            else
                ExecutionContext.Run(_executionContext, static queued => (queued as QueuedStart<T>)?.Start(), this);
        }

        private void Start()
        {
            ValueTask<T> operation;
            try
            {
                operation = _start();
            }
            catch (Exception exception)
            {
                operation = ValueTask.FromException<T>(exception);
            }
            _call.Await(operation);
        }
    }
}
