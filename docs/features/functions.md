# Functions

Functions are one of the main ways managed code and Luau call into each other.

You usually work in two directions:

- call an existing Luau function from C#,
- expose a managed callback so Luau can call into your code.

## Call Luau functions from C\#

Get an owned `LuauFunction` from globals or a table, keep it for as long as needed, and invoke it with typed arguments:

```csharp
using LuauFunction add = lua.Globals.GetLuauFunction("add");
double result = add.Invoke<double>(1, 2);

using LuauFunction pair = lua.Globals.GetLuauFunction("pair");
(int sum, int difference) = pair.Invoke<int, int>(20, 4);
```

The generic return type controls how the Luau return value is converted.

- Use `Invoke(...)` when you want to ignore return values.
- Use `Invoke<TR>(...)` for a single typed return value. Extra Luau return values are ignored.
- Use `Invoke<TR1, TR2>(...)` or `Invoke<TR1, TR2, TR3>(...)` when you want multiple typed return values.
- Use `InvokeMulti(...)` when you want all Luau return values as raw `LuauValue` instances.
- Argument values go through the normal `IntoLuau` conversion rules.
- The current argument buffer accepts up to 4 arguments per call.

If Luau raises an error, `Invoke<TR>(...)` throws `LuaException`. If the return value cannot be converted to `TR`, it throws `InvalidCastException`.

Each `Invoke` overload has an `InvokeAsync` counterpart that lets managed callbacks await. See [Async callbacks](#async-callbacks).

## Expose managed callbacks with `CreateFunction(...)`

Use `CreateFunction(...)` for supported fixed delegate signatures:

```csharp
using LuauFunction log = lua.CreateFunction((string message) => Console.WriteLine(message));
lua.Globals.Set("log", log);

using LuauFunction add = lua.CreateFunction((int a, int b) => a + b);
lua.Globals.Set("add", add);

using LuauFunction pair = lua.CreateFunction((int a, int b) => (a + b, a - b));
lua.Globals.Set("pair", pair);
```

This is the normal callback API when your callback shape is simple and static.

### Direct-call requirement

`CreateFunction(...)` is generator-backed. The direct method call is intercepted at compile time and replaced with a generated marshalling adapter.

- Call `CreateFunction(...)` directly at the call site.
- Do not store `lua.CreateFunction` in another delegate and invoke it indirectly.
- There is no runtime fallback. If interception does not happen, the stub throws.

### What fits this API well

`CreateFunction(...)` is best for fixed signatures with:

- primitive numeric and boolean types,
- supported nullable value types,
- enums,
- `string` and span-based string or buffer parameters,
- `LuauValue`,
- managed userdata types, either generated with `[LuauUserdata]` or implemented manually with `ILuauUserData<TSelf>`,
- borrowed callback views such as `LuauTableView` or `LuauFunctionView`,
- `void`, one managed return value, or a top-level tuple return whose elements are individually supported,
- `Task` or `ValueTask` around any of these returns, and a `CancellationToken` parameter. See [Async callbacks](#async-callbacks).

Managed userdata support here is the typed managed path, not the raw userdata wrapper path. Use `LuauUserdataView` when you want a borrowed userdata view directly; use `[LuauUserdata]` or `ILuauUserData<TSelf>` when you want `CreateFunction(...)` to marshal to and from your managed type.

The supported signature set is narrower than the library's overall type-conversion surface. Generator-backed callbacks currently reject nested tuple returns and are limited to top-level tuple returns that fit the current `LuauReturn.Ok(...)` arity. If a delegate shape is not supported there, use `CreateFunctionBuilder(...)` instead. See [Type mapping](../concepts/type-mapping.md) for the broader conversion model.

## Choose between callback APIs

| Capability | `CreateFunction(...)` | `CreateFunctionBuilder(...)` |
| --- | --- | --- |
| Input shape | typed delegate | `LuauArgs` |
| Output shape | `void`, one managed return value, or a supported top-level tuple return | `LuauReturn.Ok(...)` / `LuauReturn.Error(...)` |
| Requirements | direct call, generator-backed | plain runtime API |
| Best for | simple fixed signatures, including supported tuple returns | manual validation, custom errors, unsupported signatures |

Prefer `CreateFunction(...)` unless you specifically need the extra control from `CreateFunctionBuilder(...)`.

## Use `CreateFunctionBuilder(...)` for manual callbacks

Use `CreateFunctionBuilder(...)` when you want to parse callback arguments yourself, shape the user-facing error contract explicitly, or expose a callback shape the generator does not support:

```csharp
using LuauFunction pair = lua.CreateFunctionBuilder(static args =>
{
    if (!args.TryValidateArgumentCount(2, out string? error))
        return LuauReturn.Error(error);
    if (!args.TryReadNumber(1, out int a, out error) || !args.TryReadNumber(2, out int b, out error))
        return LuauReturn.Error(error);
    if (a <= b)
        return LuauReturn.Error("Expected a to be greater than b");

    return LuauReturn.Ok(a + b, a - b);
});

lua.Globals.Set("pair", pair);
```

`TryValidateArgumentCount(...)` checks minimum arity, not exact arity. If your callback requires an exact argument count, compare `args.ArgumentCount` yourself as well.

## Borrowed callback values

`LuauArgs` can return borrowed callback-scoped views such as `LuauFunctionView`, `LuauTableView`, `LuauStringView`, `LuauBufferView`, and `LuauUserdataView`.

Use them immediately, or promote them to owned references before the callback returns:

```csharp
using LuauFunction invokeCallback = lua.CreateFunctionBuilder(static args =>
{
    if (!args.TryReadLuauFunction(1, out LuauFunctionView callback, out string? error))
        return LuauReturn.Error(error);

    using LuauFunction owned = callback.ToOwned();
    owned.Invoke();
    return LuauReturn.Ok();
});
```

Borrowed views are valid only while the current callback frame is active. See [Lifetimes and ownership](../concepts/lifetimes.md).
For string- and buffer-specific callback shapes and ownership rules, see [Strings](strings.md) and [Buffers](buffers.md).

## Async callbacks

A `CreateFunction(...)` delegate can return `Task`, `Task<T>`, `ValueTask`, or `ValueTask<T>`. The script waits for it without blocking a thread and receives the awaited result:

```csharp
using LuauFunction delay = lua.CreateFunction(
    (int milliseconds, CancellationToken cancellationToken) => Task.Delay(milliseconds, cancellationToken)
);
lua.Globals.Set("delay", delay);

await lua.Load("delay(100)").ExecuteAsync([], cancellationToken);
```

A `CancellationToken` parameter is not a Luau argument. It receives the token of the async host call.

A callback built with `CreateFunctionBuilder(...)` awaits by returning `LuauReturn.Await(...)` with work that completes later:

```csharp
using LuauFunction delay = lua.CreateFunctionBuilder(static args =>
{
    if (!args.TryReadNumber(1, out int milliseconds, out string? error))
        return LuauReturn.Error(error);
    return LuauReturn.Await(DelayAsync(milliseconds, args.CancellationToken));

    static async ValueTask<LuauReturn> DelayAsync(int milliseconds, CancellationToken cancellationToken)
    {
        await Task.Delay(milliseconds, cancellationToken);
        return LuauReturn.Ok();
    }
});
lua.Globals.Set("delay", delay);

await lua.Load("delay(100)").ExecuteAsync([], cancellationToken);
```

Scripts that call such a callback must run through `ExecuteAsync(...)`, `InvokeAsync(...)`, or `LuauCoroutine.ResumeAsync(...)`. Read all arguments before you return `LuauReturn.Await(...)`. See [Coroutines](coroutines.md).

## Error behavior

Callback failures become normal Luau errors:

- argument conversion failures become Luau errors,
- `LuauReturn.Error(...)` produces a Luau error with your message,
- thrown exceptions are caught and surfaced as managed callback failures,
- `pcall(...)` can catch those errors on the Luau side.

## Signature guidance

- Keep callback signatures narrow and explicit.
- Make nullable behavior intentional.
- Prefer plain managed values for stable contracts.
- Use `CreateFunctionBuilder(...)` when you need fine-grained validation instead of hiding it behind a wide delegate signature.
