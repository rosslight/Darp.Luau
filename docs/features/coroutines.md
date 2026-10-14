# Coroutines

A coroutine is a Luau function that can suspend itself with `coroutine.yield(...)` and continue where it left off when it is resumed. Darp.Luau exposes coroutines as host values, and builds async managed callbacks on top of them: a callback can return work that is still running, and the script waits for it without blocking a thread.

You usually work with coroutines in one of two ways:

- drive a coroutine from C# with `LuauCoroutine`,
- let scripts call async managed callbacks through `InvokeAsync(...)`, `ExecuteAsync(...)`, or `ResumeAsync(...)`.

## Create and resume a coroutine

Create a coroutine from a `LuauFunction` and resume it. `Resume(...)` runs the coroutine until it yields or finishes and returns the yielded or returned values:

```csharp
using LuauFunction body = lua.Load(
    """
    local start = ...
    local received = coroutine.yield(start + 1)
    return received * 2
    """
).ToFunction();
using LuauCoroutine coroutine = lua.CreateCoroutine(body);

int first = coroutine.Resume<int>(1);   // 2, the value passed to coroutine.yield
int last = coroutine.Resume<int>(10);   // 20, the return value; 10 is what coroutine.yield returned
```

The arguments of the first resume become the arguments of the function. The arguments of every later resume become the results of the `coroutine.yield(...)` call the coroutine is suspended in.

`Resume(...)` follows the return-shaping pattern of `Invoke(...)`: use `Resume()` to ignore values, `Resume<TR>(...)` up to `Resume<TR1, TR2, TR3, TR4>(...)` for typed values, and `ResumeMulti(...)` for raw `LuauValue[]` access.

## Status

`Status` reports where the coroutine is in its lifecycle:

| Status | Meaning | Can be resumed |
| --- | --- | --- |
| `Suspended` | not started yet, yielded, or waiting for an awaiting callback | yes, unless it is waiting for a callback |
| `Running` | a host call or another coroutine is executing it | no |
| `Finished` | its function returned, its host call was cancelled, or a script closed it | no |
| `Error` | it stopped with an error | no |

`Running` also covers what `coroutine.status(...)` calls `normal`: a coroutine that resumed another coroutine and waits for it.

Resuming a coroutine that is not `Suspended` throws `InvalidOperationException`. An error inside the coroutine throws `LuaException` from `Resume(...)`, and the coroutine ends in `Error`.

## Coroutines created by scripts

A coroutine created with `coroutine.create(...)` is a normal Luau value. Read it like any other reference:

```csharp
lua.Load("co = coroutine.create(function(x) coroutine.yield(x) return 'done' end)").Execute();

using LuauCoroutine coroutine = lua.Globals.Get<LuauCoroutine>("co");
int value = coroutine.Resume<int>(5);
string result = coroutine.Resume<string>();
```

- Tables read them with `Get<LuauCoroutine>(...)` and `TryGet(..., out LuauCoroutine coroutine)`.
- `LuauValue` reports coroutines as `LuauValueType.Thread` and converts them with `TryGet(out LuauCoroutine coroutine)`.
- Managed callbacks read them with `args[1].Get<LuauCoroutineView>()`, a borrowed view. Call `ToOwned()` to keep or resume it, or read `args[1].Get<LuauCoroutine>()` for an owned reference right away.

`LuauCoroutine` converts to `IntoLuau`, so you can pass it to scripts, which can resume it with `coroutine.resume(...)` themselves.

`LuauCoroutine` is an owned reference. Dispose it when you no longer need it.

## Async managed callbacks

A managed callback built with `CreateFunctionManual(...)` finishes later by creating an awaiter and handing it the work:

```csharp
using LuauFunction fetch = lua.CreateFunctionManual(args =>
{
    string url = args[1].Get<string>();
    LuauAwaiter awaiter = args.CreateAwaiter();

    return awaiter.Await(
        new ValueTask<string>(httpClient.GetStringAsync(url, args.CancellationToken)),
        static body => LuauReturn.Ok(body)
    );
});
lua.Globals.Set("fetch", fetch);
```

For the script, `fetch(url)` is a normal call that returns the body. While the work is running, the script is suspended and no thread is blocked.

- Create the awaiter before you start the work. `CreateAwaiter()` throws where the callback cannot suspend the script, so work that nobody could await is never started. See [Where a callback can await](#where-a-callback-can-await).
- `awaiter.Await(pending, complete)` converts the value of the work when it completes. `awaiter.Await(pending)` takes a `ValueTask` and returns no values, or a `ValueTask<LuauReturn>` when the work builds the result itself.
- If the work has already completed successfully, its result is returned directly and the script does not suspend.
- A userdata method and the `Call` metamethod await in the same way, with their `LuauArgs`. Getters, setters and the other metamethods cannot await.
- `CreateFunction(...)` delegates, generated `[LuauModule]` functions, and generated `[LuauUserdata]` methods do all of this for you when they return `Task` or `ValueTask`. See [Async callbacks](functions.md#async-callbacks) and [Generated async methods](userdata.md#generated-async-methods).

## Run scripts that await

A script can only be suspended by an awaiting callback if the host runs it asynchronously:

```csharp
string page = await lua.Load("return fetch('https://example.com')").ExecuteAsync<string>([], cancellationToken);

using LuauFunction handler = lua.Globals.Get<LuauFunction>("handler");
await handler.InvokeAsync([42], cancellationToken);

await coroutine.ResumeAsync([], cancellationToken);
```

- `ExecuteAsync(...)` and `InvokeAsync(...)` run the chunk or function on a new coroutine and complete when it returns. They offer the same typed overloads as `Execute(...)` and `Invoke(...)`.
- `ResumeAsync(...)` resumes an existing coroutine and completes when it yields on its own or finishes.
- Every async method has an overload with `params` arguments and one with a collection of arguments and a `CancellationToken`.
- If no callback awaits, the returned task is already completed when the method returns.
- A script that calls `coroutine.yield(...)` outside of its own coroutines fails `ExecuteAsync(...)` and `InvokeAsync(...)` with `LuaException`, like it fails `Execute(...)`.

## Where a callback can await

A callback can suspend the script only when both of these hold:

- an async host call drives the coroutine: `ExecuteAsync(...)`, `InvokeAsync(...)`, or `ResumeAsync(...)`,
- Luau can yield at that point.

Otherwise `CreateAwaiter()` throws `InvalidOperationException`, which reaches the script as a Luau error that `pcall(...)` can catch. Generated callbacks get the awaiter before they call your method, so the method does not run. That is the case

- during `Execute(...)`, `Invoke(...)`, or the sync `Resume(...)`, including an `Invoke(...)` that another callback makes,
- inside a coroutine that a script created and resumes itself, for example through `coroutine.wrap(...)`,
- inside a metamethod such as `__index` or `__lt`, a `table.sort` comparator, or a `string.gsub` replacement function.

This does not depend on timing: a callback that awaits fails in these places even when its work would already be complete.

## While a callback awaits

A coroutine that waits for a callback is suspended, and only the host continues it, with the result of the work. A script that holds the coroutine cannot take that over:

- `coroutine.resume(...)` on it fails with `cannot resume a coroutine that is waiting for a managed callback`. The error is raised inside the waiting coroutine, where the call to the callback was.
- `coroutine.close(...)` on it ends it.

In both cases the async host call fails with `LuaException`, and the result of the work is dropped when it arrives. The host call no longer runs the coroutine, so a callback cannot await in whatever the coroutine does next.

## Read arguments before the first await

`LuauArgs` and borrowed views such as `LuauFunctionView` are valid only while the callback runs. The awaited work runs after the callback has returned, so it cannot use them:

- read every argument before you hand the work to the awaiter,
- copy strings and buffers into managed values,
- promote Luau references you need afterwards with `ToOwned()`, after the awaiter was granted, and dispose them in the work.

```csharp
using LuauFunction later = lua.CreateFunctionManual(args =>
{
    LuauFunctionView callback = args[1].Get<LuauFunctionView>();
    LuauAwaiter awaiter = args.CreateAwaiter();

    return awaiter.Await(CallLaterAsync(callback.ToOwned()));

    static async ValueTask<LuauReturn> CallLaterAsync(LuauFunction callback)
    {
        using (callback)
        {
            await Task.Delay(100);
            return LuauReturn.Ok(callback.Invoke<int>(41));
        }
    }
});
```

The compiler enforces most of this: `LuauArgs` and views are `ref struct` types and cannot be captured by async methods.

## Errors

- An exception from the awaited work or from the conversion of its value becomes a Luau error at the call site, with its message. `pcall(...)` can catch it. See [Error behavior](functions.md#error-behavior).
- An error the script does not catch fails the async method with `LuaException`.
- A script that yields out of `ExecuteAsync(...)` or `InvokeAsync(...)` fails the method with `LuaException`. Its coroutine is finished, so a script that kept it cannot continue it later.

## Cancellation

The `CancellationToken` passed to `ExecuteAsync(...)`, `InvokeAsync(...)`, or `ResumeAsync(...)` stops the script, also one that never calls the host:

```csharp
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
try
{
    await lua.Load("while true do end").ExecuteAsync([], timeout.Token);
}
catch (OperationCanceledException)
{
    // The script was stopped. The state is still usable.
}
```

Cancel the token from any thread or timer. Luau checks it at its safepoints: when a loop jumps back, when the script calls a function or returns from one, and inside string pattern matching. When the token is cancelled,

- the script is stopped at its next safepoint,
- the async method throws `OperationCanceledException`. It does so too when the script fails with an error after the token was cancelled,
- the coroutine is finished. One driven by `ResumeAsync(...)` reports `Finished` afterwards, and resuming it throws `InvalidOperationException`,
- the state remains usable.

A script that is checked for cancellation runs slower, by a few nanoseconds per safepoint. A call without a token, or with `CancellationToken.None`, does not pay for it.

### What the script sees

Usually nothing. Stopping is not an error: `pcall(...)` in the script cannot catch it, and no code of the script runs afterwards.

Luau can only stop a script like that where it could also yield. Inside a metamethod such as `__index` or `__lt`, a `table.sort` comparator, a `string.gsub` replacement function, an `xpcall` handler, or a sync host call made by a callback, the script is stopped with the Luau error `script was interrupted` instead.

- A script can catch that error with `pcall(...)`, but it cannot go on: the code after the `pcall(...)` runs up to its next safepoint and is stopped there.
- Uncaught, the error ends the script, and the async method throws `OperationCanceledException` all the same.

### Callbacks and awaited work

The token is also available to callbacks as `args.CancellationToken`. Cancelling it does not stop a callback that is running, and the library does not stop waiting for awaited work on its own. Pass the token on to the work you await.

- Work that ends with `OperationCanceledException` after the token was cancelled finishes the coroutine, and the async method throws that exception.
- Work that ignores the token is awaited to its end. The script receives its result and is stopped at its next safepoint.
- Work that is cancelled for another reason, such as a timeout of its own, fails like any other work: the script receives a Luau error that `pcall(...)` can catch.

### Host calls made by a callback

Only async host calls take a token, and each is stopped by its own token only.

- An async host call that a callback makes is not stopped by the token of the call that runs the callback. Pass `args.CancellationToken` to it.
- `Invoke(...)`, `Execute(...)`, and `Resume(...)` take no token. Made by a callback, they are part of the async host call that runs the callback: its token stops them, and they throw `OperationCanceledException`. A callback that such a call reaches gets the same token as `args.CancellationToken`.
- That holds while the callback itself runs, and while the result of its awaited work is converted. It does not hold inside the awaited work: a sync host call that the work makes after an `await` is not stopped. Make an async host call with the token there.
- Made by your own code, outside an async host call, they are not stopped. Use the async methods where a script must be stoppable.

### What is not stopped

- Only safepoints are checked. Work that Luau does without reaching one runs to its end first, such as `string.rep` with a large count or a pattern that scans a long string without backtracking.
- A managed callback that is running. It reads the token itself.

Three more details:

- A script module that is stopped while it loads fails its `require(...)` with a Luau error. A script that catches it is stopped at its next safepoint.
- A coroutine that the script created and that was running when the script was stopped stays in status `normal`. It cannot be resumed.
- Such a coroutine is stopped without an error also where the code that resumed it cannot be. Luau then raises the error `attempt to break across metamethod/C-call boundary` in that code, which is stopped at its next safepoint.

## Threading

A `LuauState` runs in turns: synchronous stretches of execution, of which exactly one runs at a time. Every call that runs a script is part of a turn.

- An async host call (`ExecuteAsync(...)`, `InvokeAsync(...)`, `ResumeAsync(...)`) on an idle state runs on the calling thread until the script finishes or waits for a callback. If nothing waits, the call completes without any thread switch.
- Managed callbacks run inside that turn. While they run, the state installs its own `SynchronizationContext`, so every `await` in a callback's work captures it.
- Code after such an `await` is queued and runs in a later turn, never while another turn executes Luau. It may therefore use the state: invoke Luau functions, create tables, or return owned references with `LuauReturn.Ok(...)`.
- Queued turns run on a thread-pool thread, one after another.
- An async host call that starts while another thread executes a turn is queued and starts in a later turn. Its arguments are copied when you call it; reference arguments such as a `LuauTable` must stay alive until the call completes. A start queued because another thread owns the state resolves its function or coroutine handle when its turn runs, so keep those handles alive until the returned task has completed.
- An async host call completes when the turn that finished it has ended. Your code after `await lua.Load(...).ExecuteAsync(...)` runs on your own context, or on a thread-pool thread, and can use the state right away.

### Synchronous calls

`Execute(...)`, `Invoke(...)`, `Resume(...)`, reading and writing a table by key, and `Dispose()` take the turn for as long as they run. They never wait for it:

- Inside a turn, that is, in a managed callback or in the code after its captured `await`, they are part of that turn.
- On an idle state they run on the calling thread. Work that async calls queue in the meantime runs when they have returned, and so does an async host call that one of their callbacks starts, once it waits for a callback.
- While another thread executes a turn, they throw `InvalidOperationException` and leave the state as it is.

Without a host dispatcher, an async call that is in flight runs its next turn on a thread-pool thread at any moment, and a synchronous call from your own thread fails if it meets that turn. Make it when the async calls of the state have completed, or pass a host dispatcher.

The rest of the synchronous API is not checked: creating values such as tables and strings, enumerating a table, and disposing owned references. Use it inside a turn, or when no other thread can execute one.

### Host dispatcher

Pass a single-threaded dispatcher, such as a UI thread's `SynchronizationContext`, to run all turns on that thread:

```csharp
// On the UI thread: async calls and continuations of async callbacks run on the UI thread.
var lua = new LuauState(LuauLibraries.All, null, SynchronizationContext.Current);
```

- Start async calls on the dispatcher thread. Calls started on another thread are posted to the dispatcher and start there.
- A synchronous call on the dispatcher thread gets the turn whatever is in flight, as long as no other thread uses the state: what is queued for the dispatcher runs after it.
- The state never picks up `SynchronizationContext.Current` on its own: a context does not guarantee that it runs one thing at a time. Only pass a context that runs its work on a single thread.

If the host dispatcher throws when work is posted to it, for example because it has shut down, a call that could not start fails with that exception and the state stays usable. A call that was already waiting for a callback cannot continue anywhere else and never completes, so await in-flight calls before you shut the dispatcher down.

### Disposing the state

`Dispose()` closes the state and ends its async host calls. Every call that is in flight fails with `ObjectDisposedException`, whether the work its callback awaits has finished or not. That work is not stopped: it goes on without the state, and its result is dropped. Cancel the token of a call to stop its work.

`Dispose()` throws `InvalidOperationException`

- while the state runs a script. That is the case inside every callback of it: managed functions, userdata members, module loaders, and the file system that `require(...)` uses. Luau would continue in a closed state otherwise. Dispose the state after the host call that runs the script has returned.
- while another thread executes a turn, like every synchronous call.

### Code that leaves the turn

Some code runs outside the state's turns:

- code after `await ...ConfigureAwait(false)` inside a callback's work,
- the body of `Task.Run(...)` and other work started on other threads,
- continuations of custom awaiters that ignore `SynchronizationContext`.

It uses the state like any other thread does: its synchronous calls fail while a turn executes, and what is not checked must wait until the code is back after a captured `await`.

Do not block on async work inside a turn, for example with `.Result` or `.Wait()` on a nested `InvokeAsync(...)`: the nested call needs a later turn, which cannot start while the current one blocks. That holds in the callbacks of a synchronous call too.

Do not resume a coroutine from a script while the host is awaiting a callback inside it. The resume fails and ends the host call; see [While a callback awaits](#while-a-callback-awaits).
