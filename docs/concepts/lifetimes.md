# Lifetimes and ownership

Darp.Luau makes lifetime rules explicit. Every Luau-backed value belongs to exactly one `LuauState`, and the API distinguishes between owned references, borrowed views, borrowed spans, and managed copies.

## The mental model

| Kind | Examples | Backing storage | Valid until |
| --- | --- | --- | --- |
| Owned references | `LuauTable`, `LuauFunction`, `LuauCoroutine`, `LuauString`, `LuauBuffer`, `LuauUserdata`, reference-backed `LuauValue` | tracked registry reference | you dispose it, or the state is disposed |
| Borrowed views | `LuauTableView`, `LuauFunctionView`, `LuauCoroutineView`, `LuauStringView`, `LuauBufferView`, `LuauUserdataView`, `LuauArgs`, `LuauArgsSingle` | current callback stack frame | the callback returns |
| Borrowed spans | `ReadOnlySpan<byte>` from string or buffer reads | Luau-owned memory | only while the aliased memory stays valid |
| Managed copies | `string`, `byte[]`, numbers, booleans | managed memory | normal .NET lifetime |

`LuauState` is the outer lifetime boundary. Dispose the state and every wrapper from that state becomes invalid. You also cannot use a reference from one state in another state.

## Owned references

Owned references are the values you can keep after the current operation finishes.

- They are backed by registry references tracked by the state.
- They can outlive a callback.
- They should normally be wrapped in `using`.

Typical pattern:

```csharp
using LuauFunction add = lua.Globals.GetLuauFunction("add");
double value = add.Invoke<double>(1, 2);
```

`LuauValue` also participates in this model. If it represents `table`, `function`, `thread`, `string`, `userdata`, or `buffer`, it owns a tracked reference and should be disposed.

That also applies to values returned from `InvokeMulti(...)` or `ExecuteMulti()`: dispose each returned `LuauValue` when it may be reference-backed.

Generated callbacks are the exception, because the generated code stands between Luau and your method:

| `LuauValue` in a generated callback | Who releases it |
| --- | --- |
| parameter | the generated code, when the call is over (for an async callback: when its task completes) |
| return value | the generated code, after Luau received it |
| value you read yourself, for example with `TryGet(out LuauValue copy)` | you |

This covers `CreateFunction(...)` delegates, `[LuauModule]` functions, and `[LuauUserdata]` methods. With `CreateFunctionManual(...)` you read and return values yourself: a `LuauValue` from `args.TryReadLuauValue(...)` is yours to dispose, and `LuauReturn.Ok(value)` takes a reference of its own.

A typed call that fails to read one of its results, such as `Execute<LuauTable, int>()` when the second result is not a number, releases the results it had already read before it throws.

## Borrowed values

Types ending in `View`, plus `LuauArgs` and `LuauArgsSingle`, are callback-scoped.

That rule applies equally to manual callback surfaces such as `CreateFunctionManual(...)`, userdata callbacks, and generated adapters behind `CreateFunction(...)`.

- Use them immediately.
- Do not store them in fields, collections, or across async boundaries.
- If you need to keep one, promote it with `ToOwned()` before the callback returns.

```csharp
using LuauFunction capture = lua.CreateFunctionManual(static args =>
{
    if (!args.TryReadLuauTable(1, out LuauTableView table, out string? error))
        return LuauReturn.Error(error);

    using LuauTable owned = table.ToOwned();
    return LuauReturn.Ok(owned.GetNumber("value"));
});
```

This example uses `CreateFunctionManual(...)` because it exposes `LuauArgs` directly, but the same ownership rule applies whenever a callback receives borrowed views.

If you use a borrowed view after the callback frame ends, the library throws `ObjectDisposedException`.

## Arguments across awaits

A callback that awaits has returned before the awaited work runs. Its `LuauArgs` and views have ended by then, even though the script is still waiting for the result.

- Read every argument before you hand the work to the awaiter.
- Pass managed copies, or owned references created with `ToOwned()`, to the awaited work.
- Dispose those owned references in the awaited work.

`LuauArgs` and views are `ref struct` types, so the compiler already rejects capturing them in async methods. See [Coroutines](../features/coroutines.md#read-arguments-before-the-first-await).

## Borrowed spans are still borrowed

Not every temporary value has a `View` suffix. `ReadOnlySpan<byte>` returned from APIs such as `TryReadUtf8String`, `TryReadBuffer`, `LuauString.TryGet(out ReadOnlySpan<byte>)`, or `LuauBuffer.TryGet(out ReadOnlySpan<byte>)` aliases Luau memory.

Consume those spans immediately. If you need an independent lifetime, copy into a managed `string` or `byte[]`.

These spans have something that keeps their memory alive while you use them: the callback frame for arguments, the owned wrapper for `LuauString` and `LuauBuffer`. A span read straight out of a table has nothing like that, so tables only offer it through `LuauMarshal`:

```csharp
if (LuauMarshal.TryGetUtf8StringSpan(lua.Globals, "name", out ReadOnlySpan<byte> utf8))
{
    // Valid only while the table still holds this string. Do not run a script or write the table before you are done.
}
```

When the value is replaced or removed, the next Luau garbage collection can free the memory, and reading the span afterwards is undefined behavior. Prefer `GetUtf8String(...)`, `GetBuffer(...)`, or an owned `GetLuauString(...)` unless the copy matters.

For the string- and buffer-specific API shapes that produce those spans, see [Strings](../features/strings.md) and [Buffers](../features/buffers.md).

## Promotion and move semantics

- `ToOwned()` creates a new owned registry reference from a borrowed view.
- `DisposeAndToLuauValue()` transfers ownership from an owned wrapper into a `LuauValue`.

```csharp
using LuauTable table = lua.CreateTable();
LuauValue value = table.DisposeAndToLuauValue();
```

After that call, `value` owns the reference. The original wrapper has been consumed and should not be used again.

If you later do `value.TryGet(out LuauTable tableCopy)`, you now have another owned wrapper and both `value` and `tableCopy` need to be disposed.

## Special cases

- `LuauState.Globals` is backed by a pinned global-table reference. Disposing one `Globals` wrapper does not destroy the global environment; `lua.Globals` can produce another wrapper later.
- The library rejects cross-state reference usage with `InvalidOperationException`.
- `LuauState.Dispose()` throws `InvalidOperationException` while the state runs a script, which includes every callback of it. Dispose it after the host call that runs the script has returned.
- `LuauState` itself is not thread-safe. Async host calls run their callbacks' continuations one turn at a time; use the synchronous API only when no async call is outstanding, inside a turn, or on the host dispatcher's thread. See [Coroutines](../features/coroutines.md#threading).

## Practical rules

- Keep owned references in `using` blocks.
- Treat `*View` types and callback args from `CreateFunctionManual(...)`, userdata callbacks, and other callback surfaces as immediate-use values.
- Copy spans if you need managed ownership.
- Promote with `ToOwned()` before caching or reusing a borrowed value outside the current callback.
- Dispose `LuauValue` when it may contain a reference-backed value.
