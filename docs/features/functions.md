# Functions

Functions are one of the main ways managed code and Luau call into each other.

You usually work in two directions:

- call an existing Luau function from C#,
- expose a managed callback so Luau can call into your code.

## Call Luau functions from C\#

Get an owned `LuauFunction` from globals or a table, keep it for as long as needed, and invoke it with typed arguments:

```csharp
using LuauFunction add = lua.Globals.Get<LuauFunction>("add");
double result = add.Invoke<double>(1, 2);

using LuauFunction pair = lua.Globals.Get<LuauFunction>("pair");
(int sum, int difference) = pair.Invoke<int, int>(20, 4);
```

The generic return type controls how the Luau return value is converted.

- Use `Invoke(...)` when you want to ignore return values.
- Use `Invoke<TR>(...)` for a single typed return value. Extra Luau return values are ignored.
- Use `Invoke<TR1, TR2>(...)` or `Invoke<TR1, TR2, TR3>(...)` when you want multiple typed return values.
- Use `InvokeMulti(...)` when you want all Luau return values as raw `LuauValue` instances.
- Argument values go through the normal `IntoLuau` conversion rules.
- The current argument buffer accepts up to 4 arguments per call.

If Luau raises an error, `Invoke<TR>(...)` throws `LuaException`. If the return value cannot be converted to `TR`, it throws `LuaGetException`. A value the function did not return is read as `nil`.

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

The callback can be a lambda, a method, or a delegate type of your own:

```csharp
public delegate int Scale(int value);

using LuauFunction scale = lua.CreateFunction<Scale>(value => value * 2);
```

Generated code has to name the delegate type. A delegate type of your own, and its type arguments, must be `internal` or `public`. A lambda with default parameter values, `params`, or more than 16 parameters has a delegate type that only the compiler can name: convert it to a delegate type of your own.

### Default values

A parameter with a default value takes it when the script passes `nil` or leaves the argument out:

```csharp
public delegate int Scale(int value, int factor = 2);

using LuauFunction scale = lua.CreateFunction<Scale>((value, factor) => value * factor);
```

```lua
scale(21)       -- 42
scale(21, nil)  -- 42
scale(21, 3)    -- 63
```

- The default can be a number, a `bool`, a `string`, an enum value, or `null`.
- `nil` always takes the default, so `int? limit = 5` never receives `null`.
- A span or a borrowed view cannot have a default value, and `params` is not supported.

The same holds for generated `[LuauModule]` functions and `[LuauUserdata]` methods. The operands of a metamethod cannot have a default value.

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
- managed userdata types, either generated with `[LuauUserdata]` or implemented manually with `ILuauUserdata<TSelf>`,
- borrowed callback views such as `LuauTableView` or `LuauFunctionView`,
- `void`, one managed return value, or a top-level tuple return whose elements are individually supported,
- `Task` or `ValueTask` around any of these returns, and a `CancellationToken` parameter. See [Async callbacks](#async-callbacks).

A `LuauValue` parameter belongs to the call. The generated code releases it when your callback returns, or when its task completes for an async callback, so you do not dispose it. To keep it longer, take a reference of your own: `value.TryGet(out LuauTable table)` or `value.TryGet(out LuauValue copy)`.

A `LuauValue` that your callback returns is handed over: the generated code disposes it after Luau received it. Return a copy when you keep the original, for example `stored.TryGet(out LuauValue copy)`.

The same rules apply to generated `[LuauModule]` functions and `[LuauUserdata]` methods.

Managed userdata support here is the typed managed path, not the raw userdata wrapper path. Use `LuauUserdataView` when you want a borrowed userdata view directly; use `[LuauUserdata]` or `ILuauUserdata<TSelf>` when you want `CreateFunction(...)` to marshal to and from your managed type.

The supported signature set is narrower than the library's overall type-conversion surface. Generator-backed callbacks currently reject nested tuple returns and are limited to top-level tuple returns that fit the current `LuauReturn.Ok(...)` arity. If a delegate shape is not supported there, use `CreateFunctionManual(...)` instead. See [Type mapping](../concepts/type-mapping.md) for the broader conversion model.

## Choose between callback APIs

| Capability | `CreateFunction(...)` | `CreateFunctionManual(...)` |
| --- | --- | --- |
| Input shape | typed delegate | `LuauArgs` |
| Output shape | `void`, one managed return value, or a supported top-level tuple return | `LuauReturn.Ok(...)` |
| Requirements | direct call, generator-backed | plain runtime API |
| Best for | simple fixed signatures, including supported tuple returns | arguments of more than one type, unsupported signatures |

Prefer `CreateFunction(...)` unless you specifically need the extra control from `CreateFunctionManual(...)`.

## Use `CreateFunctionManual(...)` for manual callbacks

Use `CreateFunctionManual(...)` when you want to read callback arguments yourself, or expose a callback shape the generator does not support:

```csharp
using LuauFunction pair = lua.CreateFunctionManual(static args =>
{
    int a = args[1].Get<int>();
    int b = args[2].Get<int>();
    if (a <= b)
        throw new ArgumentException("Expected a to be greater than b");

    return LuauReturn.Ok(a + b, a - b);
});

lua.Globals.Set("pair", pair);
```

`args[1]` is the first argument, a `LuauValueView`: a borrowed view of a value whose type you have not read yet.

| Read | An argument that is `nil` or was not passed | Another type |
| --- | --- | --- |
| `args[1].Get<int>()` | Luau error | Luau error |
| `args[1].Get<int?>()` | `null` | Luau error |
| `args[1].GetOrNull<string>()` | `null` | Luau error |
| `args[1].TryGet(out int value)` | `false` | `false` |
| `args[1].TryGetOrNull(out string? value)` | `true` with `null` | `false` |

A read that fails throws `LuaGetException`. You do not catch it: like every exception of a callback, it reaches the script as a Luau error with its message, such as `invalid argument #2 (number expected, got string)` or `missing argument #2 (number expected)`. See [Error behavior](#error-behavior). Use `TryGet` when an argument may have more than one type, and `args[1].Type` to look at its type without reading it.

A callback is not told when a script passes more arguments than it reads. `args.Count` is the number that was passed.

## Borrowed callback values

An argument can be read as a borrowed callback-scoped view such as `LuauFunctionView`, `LuauTableView`, `LuauStringView`, `LuauBufferView`, and `LuauUserdataView`.

Use them immediately, or promote them to owned references before the callback returns:

```csharp
using LuauFunction invokeCallback = lua.CreateFunctionManual(static args =>
{
    LuauFunctionView callback = args[1].Get<LuauFunctionView>();

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

An `async` lambda that is converted to `Action` would be `async void`: the script could not wait for it. The generator rejects that where it can see the lambda or method; a delegate you hold in a variable is not checked.

A callback built with `CreateFunctionManual(...)` gets an awaiter before it starts its work:

```csharp
using LuauFunction delay = lua.CreateFunctionManual(static args =>
{
    int milliseconds = args[1].Get<int>();
    LuauAwaiter awaiter = args.GetAwaiter();
    return awaiter.Await(new ValueTask(Task.Delay(milliseconds, args.CancellationToken)));
});
lua.Globals.Set("delay", delay);

await lua.Load("delay(100)").ExecuteAsync([], cancellationToken);
```

Scripts that call such a callback must run through `ExecuteAsync(...)`, `InvokeAsync(...)`, or `LuauCoroutine.ResumeAsync(...)`. Under a sync call `GetAwaiter()` throws, so the callback fails without starting its work. Read all arguments before you await. See [Coroutines](coroutines.md).

## Error behavior

A callback fails by throwing. The message of the exception becomes the Luau error, which a script can catch with `pcall(...)`. This is the same for every kind of callback: a `CreateFunction(...)` delegate, a `CreateFunctionManual(...)` callback, a member of a userdata type, a function of a module, and the awaited work of an async callback.

```csharp
using LuauFunction withdraw = lua.CreateFunction(
    (int balance, int amount) =>
        amount <= balance ? balance - amount : throw new ArgumentException("not enough money")
);
lua.Globals.Set("withdraw", withdraw);
```

```lua
local ok, message = pcall(withdraw, 10, 25)
print(ok, message) -- false   not enough money
```

- Any exception type works. Throw the one you would throw in other C# code, such as `ArgumentException` for an argument you reject.
- The script sees the message and nothing else. An exception without a message is reported by the name of its type.
- An argument that cannot be read throws for you. Its message names the argument, what was expected and what was passed.
- A nullable parameter of a `CreateFunction(...)` delegate may be omitted by the script and is then `null`.
- When the script does not catch the error, the host call that ran it (`Execute`, `Invoke`, ...) throws `LuaException`. Its message contains the error, behind the name and the status of the call that failed.

An exception you did not intend reaches the script in the same way, for example `Object reference not set to an instance of an object.` A script cannot tell it from an error you raised on purpose. Messages that .NET builds for you name C# parameters: `ArgumentOutOfRangeException.ThrowIfNegative(value)` gives `value ('-1') must be a non-negative value. (Parameter 'value')`. Pass your own text when the message is meant for the script author.

For a failure a script is expected to handle, follow the Luau convention and return it instead of raising it:

```csharp
using LuauFunction find = lua.CreateFunctionManual(static args =>
{
    string name = args[1].Get<string>();
    return Devices.TryGetValue(name, out Device? device)
        ? LuauReturn.Ok(device.Address)
        : LuauReturn.Ok(default, $"no device named {name}");
});
```

```lua
local address, message = find("kitchen")
if not address then
    print(message)
end
```

## Signature guidance

- Keep callback signatures narrow and explicit.
- Make nullable behavior intentional.
- Prefer plain managed values for stable contracts.
- Use `CreateFunctionManual(...)` when you need fine-grained validation instead of hiding it behind a wide delegate signature.
