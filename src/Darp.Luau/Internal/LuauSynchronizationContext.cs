using System.Runtime.CompilerServices;

namespace Darp.Luau.Internal;

/// <summary>
/// Runs the async work of one <see cref="LuauState"/> in turns: synchronous stretches of execution of which exactly
/// one runs at a time.
/// </summary>
/// <remarks>
/// <para>
/// A turn is owned by a scope. Either an async host call admits its calling thread inline because the state is idle
/// (<see cref="TryEnter"/>), or a drain executes queued work. Every continuation that captured this context is
/// posted to the queue and runs in a later turn, so code after an <c>await</c> in a managed callback never runs
/// while another turn executes Luau.
/// </para>
/// <para>
/// Drains run on a thread-pool worker, or on the host dispatcher when one was given. A drain executes queued items
/// until the queue is empty and then gives up ownership. This class is the only place in the library that
/// synchronizes threads; its lock guards ownership and the queue, never user code.
/// </para>
/// </remarks>
internal sealed class LuauSynchronizationContext : SynchronizationContext
{
    private readonly Lock _gate = new();
    private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly SynchronizationContext? _hostContext;

    // True while a scope owns the state or a drain is scheduled or running. Guarded by _gate.
    private bool _isActive;

    // The managed thread executing the current turn, 0 if none. Written under _gate, read lock-free only by the
    // thread that may have written it, to detect reentry.
    private int _ownerThreadId;

    public LuauSynchronizationContext(SynchronizationContext? hostContext) => _hostContext = hostContext;

    /// <summary>
    /// Enters a turn on the calling thread, unless another thread owns the state or, with a host dispatcher, the
    /// calling thread is not the dispatcher's.
    /// </summary>
    /// <param name="turn">The turn to dispose when the synchronous part of the work is done.</param>
    /// <returns><c>false</c> when the work must be queued with <see cref="Queue{T}"/>.</returns>
    public bool TryEnter(out Turn turn)
    {
        int currentThreadId = Environment.CurrentManagedThreadId;
        if (_ownerThreadId == currentThreadId)
        {
            turn = new Turn(this, isRoot: false);
            return true;
        }

        // With a host dispatcher, every turn runs on it. Dispatchers install themselves on their thread.
        if (_hostContext is not null && !ReferenceEquals(Current, _hostContext))
        {
            turn = default;
            return false;
        }

        using (_gate.EnterScope())
        {
            if (_isActive)
            {
                turn = default;
                return false;
            }
            _isActive = true;
            _ownerThreadId = currentThreadId;
        }
        turn = new Turn(this, isRoot: true);
        return true;
    }

    /// <summary> Queues the start of async work that could not enter a turn. </summary>
    /// <param name="start">Starts the work inside a later turn.</param>
    /// <typeparam name="T">The result type of the work.</typeparam>
    /// <returns>A task that completes with the work.</returns>
    public ValueTask<T> Queue<T>(Func<ValueTask<T>> start)
    {
        var queuedStart = new QueuedStart<T>(start);
        Post(static queued => (queued as QueuedStart<T>)?.Run(), queuedStart);
        return new ValueTask<T>(queuedStart.Completion.Task);
    }

    /// <summary> Returns <paramref name="operation"/> so that its completion does not run the caller's code inside a turn. </summary>
    /// <remarks> Only allocates when the operation is still running. </remarks>
    public static ValueTask<T> Detach<T>(ValueTask<T> operation)
    {
        if (operation.IsCompleted)
            return operation;
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Forward(operation, completion);
        return new ValueTask<T>(completion.Task);
    }

    /// <summary> Queues <paramref name="d"/> to run in a later turn. Never runs it on the calling stack. </summary>
    public override void Post(SendOrPostCallback d, object? state)
    {
        using (_gate.EnterScope())
        {
            _queue.Enqueue((d, state));
            if (_isActive)
                return;
            // Reserve ownership before the drain is published, so that no thread enters inline in between.
            _isActive = true;
        }
        ScheduleDrain();
    }

    /// <summary> Not supported: a turn must not block on another turn. </summary>
    public override void Send(SendOrPostCallback d, object? state) =>
        throw new NotSupportedException("A LuauState does not support synchronous dispatch.");

    /// <summary> Returns this context: copies must share the queue to serialize. </summary>
    public override SynchronizationContext CreateCopy() => this;

    private static void Forward<T>(ValueTask<T> operation, TaskCompletionSource<T> completion)
    {
        ConfiguredValueTaskAwaitable<T>.ConfiguredValueTaskAwaiter awaiter = operation
            .ConfigureAwait(false)
            .GetAwaiter();
        if (awaiter.IsCompleted)
        {
            SetResult(awaiter, completion);
            return;
        }
        awaiter.UnsafeOnCompleted(() => SetResult(awaiter, completion));
    }

    private static void SetResult<T>(
        ConfiguredValueTaskAwaitable<T>.ConfiguredValueTaskAwaiter awaiter,
        TaskCompletionSource<T> completion
    )
    {
        try
        {
            completion.SetResult(awaiter.GetResult());
        }
        catch (Exception exception)
        {
            SetException(completion, exception);
        }
    }

    private static void SetException<T>(TaskCompletionSource<T> completion, Exception exception)
    {
        if (exception is OperationCanceledException canceled)
            completion.SetCanceled(canceled.CancellationToken);
        else
            completion.SetException(exception);
    }

    private void EndRootTurn()
    {
        bool scheduleDrain;
        using (_gate.EnterScope())
        {
            _ownerThreadId = 0;
            scheduleDrain = _queue.Count > 0;
            // With queued work, ownership passes on to the drain.
            _isActive = scheduleDrain;
        }
        if (scheduleDrain)
            ScheduleDrain();
    }

    private void ScheduleDrain()
    {
        if (_hostContext is null)
            ThreadPool.UnsafeQueueUserWorkItem(static context => context.Drain(), this, preferLocal: false);
        else
            _hostContext.Post(static context => (context as LuauSynchronizationContext)?.Drain(), this);
    }

    private void Drain()
    {
        SynchronizationContext? previousContext = Current;
        SetSynchronizationContext(this);
        bool completed = false;
        try
        {
            while (TryTakeNext(out (SendOrPostCallback Callback, object? State) work))
                work.Callback(work.State);
            completed = true;
        }
        finally
        {
            SetSynchronizationContext(previousContext);
            // An item that threw ends this drain early; hand the rest of the queue to a new one.
            if (!completed)
                EndRootTurn();
        }
    }

    private bool TryTakeNext(out (SendOrPostCallback Callback, object? State) work)
    {
        using (_gate.EnterScope())
        {
            if (_queue.TryDequeue(out work))
            {
                _ownerThreadId = Environment.CurrentManagedThreadId;
                return true;
            }
            _ownerThreadId = 0;
            _isActive = false;
            return false;
        }
    }

    /// <summary> A turn entered by <see cref="TryEnter"/>. Installs the context on the calling thread. </summary>
    internal readonly ref struct Turn : IDisposable
    {
        private readonly LuauSynchronizationContext? _context;
        private readonly SynchronizationContext? _previousContext;
        private readonly bool _isRoot;

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

    private sealed class QueuedStart<T>(Func<ValueTask<T>> start)
    {
        private readonly Func<ValueTask<T>> _start = start;

        // The caller's ambient data (AsyncLocal) flows into the queued start, like into any continuation.
        private readonly ExecutionContext? _executionContext = ExecutionContext.Capture();

        public TaskCompletionSource<T> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Run()
        {
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
                SetException(Completion, exception);
                return;
            }
            Forward(operation, Completion);
        }
    }
}
