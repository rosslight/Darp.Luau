using System.Runtime.CompilerServices;
using System.Threading.Tasks.Sources;
using Darp.Luau.Native;
using Darp.Luau.Utils;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Internal.Async;

internal abstract unsafe class LuauAsyncCoroutineFrameBase : IDisposable
{
    private readonly LuauAsyncChunkSource _source;
    private readonly IntoLuauCopied[] _args;
    private readonly CancellationTokenRegistration _cancellationRegistration;
    private readonly object _completionContinuationLock = new();
    private int _completed;
    private int _pendingCallback;
    private int _callbackCompletionKind;
    private int _completionContinuationRegistered;
    private int _completionContinuationInvoked;
    private int _ownerThreadId;
    private Action<object?>? _completionContinuation;
    private object? _completionContinuationState;
    private SynchronizationContext? _completionContinuationSynchronizationContext;
    private LuauReturn _callbackResult;
    private Exception? _callbackException;
    private int _argumentsReleased;
    private ulong _threadHandle;
    private readonly SynchronizationContext? _synchronizationContext;

    protected LuauAsyncCoroutineFrameBase(
        LuauState state,
        LuauAsyncChunkSource source,
        IntoLuauCopied[] args,
        CancellationToken cancellationToken
    )
    {
        State = state;
        _source = source;
        _args = args;
        CancellationToken = cancellationToken;
        _synchronizationContext = SynchronizationContext.Current;
        State.RegisterAsyncFrame(this);
        if (cancellationToken.CanBeCanceled && !cancellationToken.IsCancellationRequested)
            _cancellationRegistration = cancellationToken.Register(static value => ((LuauAsyncCoroutineFrameBase)value!).Cancel(), this);
    }

    public LuauState State { get; }

    public lua_State* Thread { get; private set; }

    public CancellationToken CancellationToken { get; }

    public bool IsCompleted => Volatile.Read(ref _completed) != 0;

    public void Start()
    {
        if (IsCompleted)
            return;
        if (State.IsDisposed)
        {
            CompleteException(new ObjectDisposedException(nameof(LuauState)));
            return;
        }
        if (CancellationToken.IsCancellationRequested)
        {
            CompleteCanceled();
            return;
        }

        try
        {
            _ownerThreadId = Environment.CurrentManagedThreadId;
            Thread = lua_newthread(State.L);
            if (Thread is null)
                throw new InvalidOperationException("Could not create Luau coroutine.");
            _threadHandle = State.ReferenceTracker.TrackAndPopRef(State.L, -1);

            _source.LoadCompiledChunk(State, Thread);
            for (int i = 0; i < _args.Length; i++)
                _args[i].Push(State, Thread);
            ReleaseArguments();

            int status;
            using (State.EnterAsyncFrame(this))
            {
                status = lua_resume(Thread, State.L, _args.Length);
            }
            HandleResumeStatus(status);
        }
        catch (Exception exception)
        {
            CompleteException(exception);
        }
    }

    public bool TryBeginPendingCallback() => Interlocked.Exchange(ref _pendingCallback, 1) == 0;

    public void EnqueueCallbackResult(LuauReturn result)
    {
        Volatile.Write(ref _pendingCallback, 0);
        _callbackResult = result;
        _callbackException = null;
        Volatile.Write(ref _callbackCompletionKind, 1);
        SignalCompletionContinuationIfRegistered();
    }

    public void EnqueueCallbackException(Exception exception)
    {
        if (exception is OperationCanceledException && CancellationToken.IsCancellationRequested)
        {
            EnqueueOnCapturedContext(CompleteCanceled);
            return;
        }

        Volatile.Write(ref _pendingCallback, 0);
        _callbackException = exception;
        Volatile.Write(ref _callbackCompletionKind, 2);
        SignalCompletionContinuationIfRegistered();
    }

    public void CompleteDisposed() => CompleteException(new ObjectDisposedException(nameof(LuauState)));

    public int PushCallbackCompletion(lua_State* luaState)
    {
        int kind = Interlocked.Exchange(ref _callbackCompletionKind, 0);
        if (kind == 1)
            return LuauStateMarshal.ReturnAsyncCallbackResultMarker(State, luaState, _callbackResult);
        if (kind == 2)
        {
            Exception exception = _callbackException ?? new InvalidOperationException("Unknown async callback failure.");
            return LuauStateMarshal.ReturnAsyncCallbackErrorMarker(
                luaState,
                $"managed function callback failed: {exception.GetType().Name}: {exception.Message}"
            );
        }

        return LuauStateMarshal.ReturnAsyncCallbackErrorMarker(
            luaState,
            "async managed function continuation resumed without a callback result"
        );
    }

    protected void ResumePendingCallbackIfReady()
    {
        if (Volatile.Read(ref _callbackCompletionKind) == 0 || IsCompleted)
            return;

        State.EnqueueAsyncWork(ResumePendingCallbackCore);
    }

    protected void MarkCompletionContinuationRegistered()
    {
        Volatile.Write(ref _completionContinuationRegistered, 1);
    }

    protected void RegisterCompletionContinuation(
        Action<object?> continuation,
        object? state,
        ValueTaskSourceOnCompletedFlags flags
    )
    {
        ArgumentNullException.ThrowIfNull(continuation);

        lock (_completionContinuationLock)
        {
            _completionContinuation = continuation;
            _completionContinuationState = state;
            if ((flags & ValueTaskSourceOnCompletedFlags.UseSchedulingContext) != 0)
                _completionContinuationSynchronizationContext = SynchronizationContext.Current;
        }

        MarkCompletionContinuationRegistered();
        if (IsCompleted || Volatile.Read(ref _callbackCompletionKind) != 0)
            SignalCompletionContinuationIfRegistered();
    }

    protected void SignalCompletionContinuationIfRegistered()
    {
        if (Volatile.Read(ref _completionContinuationRegistered) == 0)
            return;

        if (Interlocked.Exchange(ref _completionContinuationInvoked, 1) != 0)
            return;

        Action<object?>? continuation;
        object? state;
        SynchronizationContext? synchronizationContext;
        lock (_completionContinuationLock)
        {
            continuation = _completionContinuation;
            state = _completionContinuationState;
            synchronizationContext = _completionContinuationSynchronizationContext;
        }
        if (continuation is null)
            return;

        if (synchronizationContext is not null)
        {
            synchronizationContext.Post(static callbackState =>
            {
                var (continuation, state) = ((Action<object?>, object?))callbackState!;
                continuation(state);
            }, (continuation, state));
            return;
        }

        ThreadPool.QueueUserWorkItem(static callbackState =>
        {
            var (continuation, state) = ((Action<object?>, object?))callbackState!;
            continuation(state);
        }, (continuation, state));
    }

    protected void WaitForCallbackCompletionOnOwnerThread()
    {
        if (Volatile.Read(ref _callbackCompletionKind) != 0 || Volatile.Read(ref _pendingCallback) == 0 || IsCompleted)
            return;
        if (Environment.CurrentManagedThreadId != Volatile.Read(ref _ownerThreadId))
            return;

        _ = SpinWait.SpinUntil(
            () => Volatile.Read(ref _callbackCompletionKind) != 0 || Volatile.Read(ref _pendingCallback) == 0 || IsCompleted,
            TimeSpan.FromMilliseconds(10)
        );
    }

    private void ResumePendingCallbackCore()
    {
        if (Volatile.Read(ref _callbackCompletionKind) == 0 || IsCompleted)
            return;
        if (State.IsDisposed)
        {
            CompleteException(new ObjectDisposedException(nameof(LuauState)));
            return;
        }
        if (CancellationToken.IsCancellationRequested)
        {
            CompleteCanceled();
            return;
        }

        try
        {
            int status;
            using (State.EnterAsyncFrame(this))
            {
                status = lua_resume(Thread, State.L, 0);
            }
            HandleResumeStatus(status);
        }
        catch (Exception exception)
        {
            CompleteException(exception);
        }
    }

    private void HandleResumeStatus(int status)
    {
        if (IsCompleted)
            return;

        if (status == (int)lua_Status.LUA_OK)
        {
            try
            {
                CompleteSuccess();
            }
            catch (Exception exception)
            {
                CompleteException(exception);
            }
            return;
        }

        if (status == (int)lua_Status.LUA_YIELD)
        {
            if (Volatile.Read(ref _pendingCallback) != 0)
                return;

            CompleteException(new LuaException("Lua invocation lua_resume yielded without a pending async callback."));
            return;
        }

        try
        {
            LuaException.ThrowIfNotOk(Thread, status, "lua_resume");
        }
        catch (Exception exception)
        {
            CompleteException(exception);
        }
    }

    private void CompleteSuccess()
    {
        if (TryStartComplete())
        {
            SetResultFromThread();
            Dispose();
        }
    }

    private void CompleteException(Exception exception)
    {
        if (TryStartComplete())
        {
            SetException(exception);
            Dispose();
        }
    }

    private void CompleteCanceled()
    {
        if (TryStartComplete())
        {
            SetCanceled(CancellationToken);
            Dispose();
        }
    }

    private void Cancel()
    {
        EnqueueOnCapturedContext(CompleteCanceled);
    }

    private void EnqueueOnCapturedContext(Action action)
    {
        if (_synchronizationContext is null || ReferenceEquals(SynchronizationContext.Current, _synchronizationContext))
        {
            State.EnqueueAsyncWork(action);
            return;
        }

        _synchronizationContext.Post(static value =>
        {
            var (state, action) = ((LuauState, Action))value!;
            state.EnqueueAsyncWork(action);
        }, (State, action));
    }

    private bool TryStartComplete() => Interlocked.Exchange(ref _completed, 1) == 0;

    private void ReleaseArguments()
    {
        if (Interlocked.Exchange(ref _argumentsReleased, 1) != 0)
            return;
        foreach (IntoLuauCopied arg in _args)
            arg.Release();
    }

    public void Dispose()
    {
        _cancellationRegistration.Dispose();
        ReleaseArguments();
        if (!State.IsDisposed && Thread is not null)
            lua_settop(Thread, 0);
        if (_threadHandle != 0)
        {
            State.ReferenceTracker.ReleaseRef(_threadHandle);
            _threadHandle = 0;
        }
        _source.Release(State);
        State.UnregisterAsyncFrame(this);
    }

    protected abstract void SetResultFromThread();

    protected abstract void SetException(Exception exception);

    protected abstract void SetCanceled(CancellationToken cancellationToken);
}

internal unsafe class LuauAsyncCoroutineFrame<TResult> : LuauAsyncCoroutineFrameBase, IValueTaskSource<TResult>
{
    private readonly Func<LuauArgs, TResult> _resultSelector;
    private ManualResetValueTaskSourceCore<TResult> _completion = new() { RunContinuationsAsynchronously = true };

    public LuauAsyncCoroutineFrame(
        LuauState state,
        LuauAsyncChunkSource source,
        IntoLuauCopied[] args,
        Func<LuauArgs, TResult> resultSelector,
        CancellationToken cancellationToken
    )
        : base(state, source, args, cancellationToken)
    {
        _resultSelector = resultSelector;
    }

    public ValueTask<TResult> ValueTask => new(this, _completion.Version);

    protected override void SetResultFromThread()
    {
        using LuauCallFrame frame = State.BeginLuauCallFrame();
        _completion.SetResult(_resultSelector(new LuauArgs(Thread, lua_gettop(Thread), 1, frame)));
        SignalCompletionContinuationIfRegistered();
    }

    protected override void SetException(Exception exception)
    {
        _completion.SetException(exception);
        SignalCompletionContinuationIfRegistered();
    }

    protected override void SetCanceled(CancellationToken cancellationToken)
    {
        _completion.SetException(new OperationCanceledException(cancellationToken));
        SignalCompletionContinuationIfRegistered();
    }

    public TResult GetResult(short token)
    {
        WaitForCallbackCompletionOnOwnerThread();
        ResumePendingCallbackIfReady();
        return _completion.GetResult(token);
    }

    public ValueTaskSourceStatus GetStatus(short token)
    {
        WaitForCallbackCompletionOnOwnerThread();
        ResumePendingCallbackIfReady();
        return _completion.GetStatus(token);
    }

    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        RegisterCompletionContinuation(continuation, state, flags);
        ResumePendingCallbackIfReady();
    }
}

internal sealed unsafe class LuauAsyncVoidCoroutineFrame : LuauAsyncCoroutineFrameBase, IValueTaskSource
{
    private ManualResetValueTaskSourceCore<object?> _completion = new() { RunContinuationsAsynchronously = true };

    public LuauAsyncVoidCoroutineFrame(
        LuauState state,
        LuauAsyncChunkSource source,
        IntoLuauCopied[] args,
        CancellationToken cancellationToken
    )
        : base(state, source, args, cancellationToken) { }

    public ValueTask VoidValueTask => new(this, _completion.Version);

    protected override void SetResultFromThread()
    {
        _completion.SetResult(null);
        SignalCompletionContinuationIfRegistered();
    }

    protected override void SetException(Exception exception)
    {
        _completion.SetException(exception);
        SignalCompletionContinuationIfRegistered();
    }

    protected override void SetCanceled(CancellationToken cancellationToken)
    {
        _completion.SetException(new OperationCanceledException(cancellationToken));
        SignalCompletionContinuationIfRegistered();
    }

    public void GetResult(short token)
    {
        WaitForCallbackCompletionOnOwnerThread();
        ResumePendingCallbackIfReady();
        _completion.GetResult(token);
    }

    public ValueTaskSourceStatus GetStatus(short token)
    {
        WaitForCallbackCompletionOnOwnerThread();
        ResumePendingCallbackIfReady();
        return _completion.GetStatus(token);
    }

    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        RegisterCompletionContinuation(continuation, state, flags);
        ResumePendingCallbackIfReady();
    }
}
