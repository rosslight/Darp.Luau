# Async Implementation Plan

> **Draft implementation:** this document records the intended design agreed in June 2026.
> The current branch implements the phase-2 runtime draft, with known gaps and one failing
> thread-affinity regression. Read [AsyncRuntimeStatus.md](AsyncRuntimeStatus.md) for what
> exists today, validation results, and the decisions still needed. Later phases below are
> planned work, not available API. In particular, current async callbacks must read their
> stack-backed `LuauArgs` before their first incomplete await; the longer lifetime described
> in the original plan is not implemented.

This document captures the agreed plan for supporting async Luau execution and async managed callbacks.

## Goals

- Keep sync APIs fast and predictable.
- Add async APIs as an execution context that can yield and resume Luau coroutines.
- Make async behavior match sync behavior wherever possible.
- Prefer explicit API shape over hidden async behavior.
- Keep the first implementation simple, while leaving room for scheduler and allocation optimizations later.

## Public API Shape

### Manual callbacks

Keep the existing sync builder:

```csharp
public delegate LuauReturn LuauFunctionBuilder(LuauArgs args);

public LuauFunction CreateFunctionBuilder(LuauFunctionBuilder onCalled);
```

Add a separate async builder:

```csharp
public delegate ValueTask<LuauReturn> LuauAsyncFunctionBuilder(
    LuauArgs args,
    CancellationToken token);

public LuauFunction CreateAsyncFunctionBuilder(LuauAsyncFunctionBuilder onCalled);
```

`CreateAsyncFunctionBuilder` returns the existing `LuauFunction` type. There is no separate `LuauAsyncFunction` handle. From Luau's point of view both are functions; the difference is whether the current execution context permits yielding.

### Chunk execution

Sync chunk execution keeps the existing `lua_pcall` path and current `LuauChunk` semantics. A `LuauChunk` remains an ephemeral source wrapper and recompiles on each execution.

Async chunk execution uses a coroutine frame:

```csharp
public ValueTask ExecuteAsync(params RefEnumerable<IntoLuau> args);
public ValueTask ExecuteAsync(RefEnumerable<IntoLuau> args, CancellationToken token);

public ValueTask<TR> ExecuteAsync<TR>(params RefEnumerable<IntoLuau> args);
public ValueTask<TR> ExecuteAsync<TR>(RefEnumerable<IntoLuau> args, CancellationToken token);
```

The generic and multi-return async overloads should mirror the sync `Execute<...>` overloads.

### Function invocation

`LuauFunction` should gain async invocation APIs matching the async chunk execution pattern:

```csharp
public ValueTask InvokeAsync(params RefEnumerable<IntoLuau> args);
public ValueTask InvokeAsync(RefEnumerable<IntoLuau> args, CancellationToken token);

public ValueTask<TR> InvokeAsync<TR>(params RefEnumerable<IntoLuau> args);
public ValueTask<TR> InvokeAsync<TR>(RefEnumerable<IntoLuau> args, CancellationToken token);
```

Async entrypoints are valid for purely synchronous Luau code and sync callbacks. If nothing yields, the operation may complete synchronously.

Sync entrypoints remain valid only for sync behavior. If sync execution reaches an async-only callback, the callback returns a normal Lua error. That error is catchable by `pcall`; if unhandled, the outer sync API surfaces it as `LuaException`.

## Runtime Architecture

### Execution paths

Sync APIs stay on the existing `lua_pcall` path.

Async APIs always execute through a Luau coroutine frame driven by `lua_resume`, even if no async callback is hit. This keeps async stack and yield behavior consistent.

### Per-state async pump

Each `LuauState` owns a serialized async pump. The pump is not a dedicated thread. It is a per-state queue that ensures only one item touches the Luau state at a time.

The first implementation should use a simple locked queue plus a draining flag:

```csharp
internal sealed class LuauAsyncWorkQueue
{
    private readonly Queue<LuauAsyncWorkItem> _queue = new();
    private bool _draining;

    public void Enqueue(LuauAsyncWorkItem item)
    {
        lock (_queue)
        {
            _queue.Enqueue(item);
            if (_draining)
                return;

            _draining = true;
        }

        Drain();
    }

    private void Drain()
    {
        while (true)
        {
            LuauAsyncWorkItem item;
            lock (_queue)
            {
                if (_queue.Count == 0)
                {
                    _draining = false;
                    return;
                }

                item = _queue.Dequeue();
            }

            item.RunOnLuauState();
        }
    }
}
```

The pump may drain inline on the enqueueing thread. The queue abstraction should remain internal and swappable so it can later move to channels, a host scheduler, or a dedicated thread.

The pump guarantees serialized Luau access. It should not document strict ordering between independent async operations. FIFO is acceptable as an implementation detail.

Multiple async executions may be pending on the same `LuauState`. Awaited managed work runs outside the pump; only Luau resume, stack access, result push, and cleanup run through the pump.

### Async callback flow

When an async callback is invoked:

1. The runtime creates `LuauArgs` backed by a call-frame lifetime token.
2. The callback is invoked with `LuauArgs` and the operation `CancellationToken`.
3. If the returned `ValueTask<LuauReturn>` is already complete, the result is pushed immediately.
4. If it is incomplete, the current Luau coroutine yields with zero visible Lua values.
5. The pending managed callback is stored in the async frame.
6. When the managed task completes, it posts a resume item to the state pump.
7. The pump resumes the original coroutine with the callback result or error.

The first implementation should not use Lua-visible pending sentinels. Pending state lives in managed runtime state.

### Errors

Async callback failures are reintroduced at the suspended Luau call site as Lua errors. This means `pcall` and `xpcall` inside `ExecuteAsync` / `InvokeAsync` can catch async callback failures.

Unhandled async callback errors complete the outer async operation as `LuaException`.

Managed exceptions must not escape native callback boundaries. Callback trampolines must catch managed exceptions and convert them to Lua errors through the normal callback error path.

### Cancellation

Cancellation is cooperative.

`ExecuteAsync` / `InvokeAsync` pass the provided token into async callbacks. Cancellation does not preempt currently running Luau bytecode and does not abort synchronous managed callbacks already on the stack.

External cancellation should bypass Luau `pcall` where possible and complete the outer async operation as `OperationCanceledException`. If user code catches cancellation and returns `LuauReturn.Error(...)`, that returned error is a normal Lua error.

### Reentry

Same-state async reentry is allowed only through async APIs and the async pump.

Sync state-driving calls that would start `lua_pcall` or otherwise drive the same `LuauState` while an async pump item is active should fail clearly, for example with a "state is busy; use async API" error.

Lightweight operations that are already valid in the current callback frame remain allowed.

## Lifetime Model

### LuauArgs

`LuauArgs` is not user-disposable. It should carry an internal frame or lifetime token:

```csharp
public readonly struct LuauArgs
{
    private readonly LuauCallFrame? _frame;

    private void ThrowIfDisposed()
    {
        if (_frame is null || _frame.IsDisposed)
            throw new ObjectDisposedException(nameof(LuauArgs));
    }
}
```

The runtime owns the frame:

- for sync callbacks, it is disposed when the callback returns;
- for async callbacks, it remains valid until the async callback completes.

Every `LuauArgs` read must check the frame lifetime.

Borrowed view types such as `LuauTableView`, `LuauFunctionView`, and `LuauUserdataView` stay as they are for now. Because they are `ref struct` types, they cannot escape across `await`. If that becomes limiting, add explicit owned snapshot APIs later.

### LuauReturn

`LuauReturn` remains a self-contained DTO. It should not carry state or a lifetime token.

It may be returned from async callbacks because values are copied into owned `IntoLuauCopied` storage when the result is created. The runtime is responsible for releasing copied values after push or failure.

### Disposal

Do not broaden async disposal to lightweight reference wrappers. Types such as `LuauFunction`, `LuauTable`, `LuauUserdata`, and `LuauValue` should stay synchronously disposable unless they gain cleanup that genuinely needs awaiting.

For `LuauState`, the exact public disposal interface can be finalized during implementation. The required behavior is:

- no new work after disposal starts;
- pending async frames are canceled or faulted;
- queued resume work for disposed frames becomes a no-op;
- internal coroutine references and owned Luau references are released while the state is still valid;
- late managed continuations must not touch a closed Luau state.

## Generated Exports

Async generated exports should behave like sync generated exports after unwrapping the async return type.

### Async inference

The generator infers async from the managed return type:

- `Task`
- `Task<T>`
- `ValueTask`
- `ValueTask<T>`

Manual builder APIs use `ValueTask` only. Generated exports support both `Task` and `ValueTask`.

### Return mapping

After awaiting, use the existing sync return mapping:

- `Task` / `ValueTask` maps like `void` and returns zero Luau values.
- `Task<T>` / `ValueTask<T>` maps like sync return type `T`.
- Tuple return types map to multiple Luau returns exactly like sync tuple returns.
- No async-specific `LuauReturn` escape hatch is added to generated exports. Direct `LuauReturn` control stays in manual builder APIs, matching sync behavior.

Generated async glue should use `ConfigureAwait(false)` when awaiting user methods.

### Host-injected cancellation token

For async generated exports, a final `CancellationToken` parameter is host-injected from the async callback token and does not consume a Luau argument slot:

```csharp
public static ValueTask<int> LoadAsync(string key, CancellationToken token)
```

No special diagnostics are needed beyond this accepted case. Other `CancellationToken` positions should fail through normal unsupported Luau parameter validation.

Sync exports do not get host-injected cancellation tokens.

### Modules

Generated module `OnLoad` remains synchronous.

It can register both sync and async functions:

```csharp
public static void OnLoad(LuauState state, in LuauTable module)
{
    using LuauFunction syncFunction = state.CreateFunctionBuilder(args =>
    {
        string returns = MyModule.GetName();
        return LuauReturn.Ok(returns);
    });
    module.Set("getName", syncFunction);

    using LuauFunction asyncFunction = state.CreateAsyncFunctionBuilder(async (args, token) =>
    {
        int returns = await MyModule.FetchCountAsync(token).ConfigureAwait(false);
        return LuauReturn.Ok(returns);
    });
    module.Set("fetchCount", asyncFunction);
}
```

Module loading itself stays synchronous. `require(...)` does not await host module construction.

While running under `require`, sync rules apply. Calling an async function from module top-level code should produce the normal Lua error for async-only behavior in a sync context.

### Userdata

Add a separate async userdata interface that extends the existing sync interface:

```csharp
public interface ILuauAsyncUserData<T> : ILuauUserData<T>
    where T : class
{
    static abstract ValueTask<LuauReturn> OnMethodCallAsync(
        T self,
        LuauArgs functionArgs,
        ReadOnlyMemory<char> methodName,
        CancellationToken token);
}
```

The exact `methodName` type can be finalized during implementation. It must be safe across async execution if the method name is needed after an await.

Generated userdata with any async method implements the async interface. The async hook dispatches both sync and async methods:

- sync methods complete immediately;
- async methods await;
- unknown methods return `LuauReturn.NotHandledError`.

Sync execution can still call sync methods on a userdata type that also has async methods. It fails only when the selected method is async.

Userdata properties remain synchronous only. `OnIndex` and `OnSetIndex` are not async surfaces.

## Require Rules

`require` remains synchronous.

Host module `OnLoad` remains synchronous.

Script module top-level execution remains synchronous. The current rule that a script module cannot yield during load should remain.

A required module may export async-callable functions, but calling those functions during module loading follows sync rules and produces a Lua error.

## Implementation Phases

### Phase 1: Runtime scaffolding

- Fix current stale sync callback invocation if needed.
- Add `LuauAsyncFunctionBuilder`.
- Add `CreateAsyncFunctionBuilder`.
- Introduce internal callback metadata that distinguishes sync and async callbacks.
- Introduce `LuauArgs` lifetime frame checks.
- Keep `LuauReturn` as an owned DTO.

### Phase 2: Async execution core

- Add per-state async pump with locked queue and inline drain.
- Add async coroutine frame type.
- Implement `LuauChunk.ExecuteAsync` through `lua_newthread` and `lua_resume`.
- Implement pending callback yield/resume.
- Convert async callback completion, exceptions, and cancellation into the agreed runtime behavior.
- Add disposal cleanup for pending frames.

### Phase 3: Async function invocation

- Add `LuauFunction.InvokeAsync` overloads matching async chunk execution.
- Share argument push, result selection, and error formatting with sync paths where practical.
- Ensure sync `Invoke` on async-only behavior returns a Lua error.

### Phase 4: Generated module exports

- Teach the generated export analyzer to unwrap `Task` / `ValueTask`.
- Host-inject final `CancellationToken` for async exports.
- Emit `CreateAsyncFunctionBuilder` for async module functions.
- Keep `OnLoad` synchronous.
- Add snapshot tests for mixed sync/async module exports.

### Phase 5: Generated userdata exports

- Add `ILuauAsyncUserData<T>`.
- Extend userdata registration to dispatch async method calls when async execution is active.
- Generate async userdata hook for types with async methods.
- Preserve sync dispatch for sync methods where possible.
- Add tests for mixed sync/async userdata methods and sync property behavior.

### Phase 6: Behavior tests

- Async callback completes synchronously.
- Async callback yields and resumes.
- Multiple pending async operations on one state.
- Sync execution calling async callback produces Lua error.
- `pcall` catches async callback failure.
- Unhandled async callback failure becomes `LuaException`.
- External cancellation becomes `OperationCanceledException`.
- `require` uses sync rules.
- Disposed state does not allow late continuations to touch Luau.

## Future Improvements

- Replace `TaskCompletionSource` internals with `ManualResetValueTaskSourceCore<T>` where it matters.
- Add a scheduler abstraction for host-provided dispatch.
- Add a channel-backed queue or dedicated thread scheduler for hosts that need stronger affinity.
- Consider coroutine pooling if coroutine creation becomes measurable.
- Add Lua-visible async yield values or a `yield_with`-style API.
- Consider async module loading only if there is a clear use case and the require cache semantics are redesigned.
- Add richer cancellation or interruption for long-running Luau bytecode if Luau exposes a safe hook for it.
- Revisit owned snapshot APIs for borrowed views if async callback ergonomics require it.
- Consider public coroutine APIs after the async runtime model is stable.
