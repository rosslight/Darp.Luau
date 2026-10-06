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
| `Finished` | its function returned, or its awaited work was canceled | no |
| `Error` | it stopped with an error | no |

`Running` also covers what `coroutine.status(...)` calls `normal`: a coroutine that resumed another coroutine and waits for it.

Resuming a coroutine that is not `Suspended` throws `InvalidOperationException`. An error inside the coroutine throws `LuaException` from `Resume(...)`, and the coroutine ends in `Error`.

## Coroutines created by scripts

A coroutine created with `coroutine.create(...)` is a normal Luau value. Read it like any other reference:

```csharp
lua.Load("co = coroutine.create(function(x) coroutine.yield(x) return 'done' end)").Execute();

using LuauCoroutine coroutine = lua.Globals.GetLuauCoroutine("co");
int value = coroutine.Resume<int>(5);
string result = coroutine.Resume<string>();
```

- Tables offer `GetLuauCoroutine(...)` and `TryGetLuauCoroutine(...)`.
- `LuauValue` reports coroutines as `LuauValueType.Thread` and converts them with `TryGet(out LuauCoroutine coroutine)`.
- Managed callbacks read them with `args.TryReadLuauCoroutine(...)`, which returns a borrowed `LuauCoroutineView`. Call `ToOwned()` to keep or resume it.

`LuauCoroutine` converts to `IntoLuau`, so you can pass it to scripts, which can resume it with `coroutine.resume(...)` themselves.

`LuauCoroutine` is an owned reference. Dispose it when you no longer need it.

## Async managed callbacks

A managed callback built with `CreateFunctionBuilder(...)` can return `LuauReturn.Await(...)` with work that produces its result later:

```csharp
using LuauFunction fetch = lua.CreateFunctionBuilder(args =>
{
    if (!args.TryReadUtf8String(1, out ReadOnlySpan<byte> utf8Url, out string? error))
        return LuauReturn.Error(error);

    string url = Encoding.UTF8.GetString(utf8Url);
    return LuauReturn.Await(FetchAsync(url, args.CancellationToken));

    async ValueTask<LuauReturn> FetchAsync(string url, CancellationToken cancellationToken)
    {
        string body = await httpClient.GetStringAsync(url, cancellationToken);
        return LuauReturn.Ok(body);
    }
});
lua.Globals.Set("fetch", fetch);
```

For the script, `fetch(url)` is a normal call that returns the body. While the work is running, the script is suspended and no thread is blocked.

- If the work has already completed successfully, `LuauReturn.Await(...)` returns its result directly and the script does not suspend.
- The work completes with `LuauReturn.Ok(...)` or `LuauReturn.Error(...)` like a sync callback.
- A pending result is only supported as the result of a managed function. Userdata hooks cannot await.

## Run scripts that await

A script can only be suspended by an awaiting callback if the host runs it asynchronously:

```csharp
string page = await lua.Load("return fetch('https://example.com')").ExecuteAsync<string>([], cancellationToken);

using LuauFunction handler = lua.Globals.GetLuauFunction("handler");
await handler.InvokeAsync([42], cancellationToken);

await coroutine.ResumeAsync([], cancellationToken);
```

- `ExecuteAsync(...)` and `InvokeAsync(...)` run the chunk or function on a new coroutine and complete when it returns. They offer the same typed overloads as `Execute(...)` and `Invoke(...)`.
- `ResumeAsync(...)` resumes an existing coroutine and completes when it yields on its own or finishes.
- Every async method has an overload with `params` arguments and one with a collection of arguments and a `CancellationToken`.
- If no callback awaits, the returned task is already completed when the method returns.
- A script that calls `coroutine.yield(...)` outside of its own coroutines fails `ExecuteAsync(...)` and `InvokeAsync(...)` with `LuaException`, like it fails `Execute(...)`.

Everywhere else, an awaiting callback raises a Luau error that `pcall(...)` can catch:

- during `Execute(...)`, `Invoke(...)`, or the sync `Resume(...)`,
- inside a coroutine that a script created and resumes itself, for example through `coroutine.wrap(...)`.

## Read arguments before the first await

`LuauArgs` and borrowed views such as `LuauFunctionView` are valid only while the callback runs. The awaited work runs after the callback has returned, so it cannot use them:

- read every argument before you return `LuauReturn.Await(...)`,
- copy strings and buffers into managed values,
- promote Luau references you need afterwards with `ToOwned()` and dispose them in the work.

```csharp
using LuauFunction later = lua.CreateFunctionBuilder(args =>
{
    if (!args.TryReadLuauFunction(1, out LuauFunctionView callback, out string? error))
        return LuauReturn.Error(error);

    return LuauReturn.Await(CallLaterAsync(callback.ToOwned()));

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

- An exception from the awaited work, or `LuauReturn.Error(...)`, becomes a Luau error at the call site. `pcall(...)` can catch it.
- An error the script does not catch fails the async method with `LuaException`.

## Cancellation

The `CancellationToken` passed to `ExecuteAsync(...)`, `InvokeAsync(...)`, or `ResumeAsync(...)` is available to callbacks as `args.CancellationToken`. Pass it on to the work you await. Cancellation is cooperative: the library does not stop waiting on its own.

When the awaited work ends with `OperationCanceledException`:

- the coroutine is finished; `pcall(...)` in the script cannot catch the cancellation,
- the async method throws that `OperationCanceledException`,
- a coroutine driven by `ResumeAsync(...)` reports `Finished` afterwards, and resuming it throws `InvalidOperationException`,
- the state remains usable.

Work that ignores the token completes normally, and the script continues with its result.

## Threads

A `LuauState` is used by one thread at a time; that is the responsibility of the host, for sync and async use alike. Async methods follow the usual .NET rules for where code continues after an `await`:

- With a `SynchronizationContext`, such as a UI thread, the script continues on that context.
- Without one, the script continues on the thread that completed the awaited work.

Several async invocations on one state are fine as long as the host does not run them on different threads at the same time.

Do not resume a coroutine from a script while the host is awaiting a callback inside it. The host cannot prevent `coroutine.resume(...)`, and the callback would receive the script's values instead of its own result.
