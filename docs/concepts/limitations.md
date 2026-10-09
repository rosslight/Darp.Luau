# Limitations and current boundaries

Darp.Luau already covers a useful embedding core, but some parts of the surface are still intentionally narrow.

## Current boundaries

- The main package currently targets `net10.0`.
- `LuauState` executes source through `Load(...).Execute(...)` or `LoadFile(path).Execute(...)`. There is no separate `DoFile(...)` helper.
- `LuauState` is not thread-safe. Async host calls serialize their own continuations, but the synchronous API does not check which thread calls it: without a host dispatcher, use it only when no async call is outstanding or inside a turn.
- Owned references and borrowed views are bound to a single `LuauState`; cross-state usage is invalid.
- `CreateFunction(...)` depends on generator interception, must be called directly, and has no runtime fallback.
- `LuauFunction.Invoke(...)` currently accepts up to 4 arguments per call through `RefEnumerable<IntoLuau>`.
- Typed `LuauFunction.Invoke(...)` returns currently have explicit overloads for 1, 2, 3, or 4 values; use `InvokeMulti(...)` for dynamic multi-return access.
- Typed chunk execution currently has explicit overloads for 1, 2, 3, or 4 values; use `ExecuteMulti()` for dynamic multi-return access.
- Generator-backed `CreateFunction(...)` supports top-level tuple returns, but currently rejects nested tuples and only supports tuple arities that fit the current `LuauReturn.Ok(...)` overload set.
- Source-generated `[LuauModule]` types must be partial, top-level, and non-generic. Instance module properties, fields, instance structs, and unsupported method shapes are not generated.
- Source-generated `[LuauUserdata]` types must be partial, top-level, non-generic classes. Fields, settable static properties, dotted userdata member names, a `Register` written by hand next to the attribute, and unsupported method shapes are not generated.
- Generated exports currently emit runtime C# glue only. Luau type-file output is not a documented shipped feature yet.
- File-backed `require(...)` is available through `EnableScriptModules()`, but it requires explicit setup and a matching chunk-name convention for file entrypoints.
- `EnableScriptModules()` currently expects script modules to return exactly one value and not yield while loading.
- Luau has one number type, a `double`. Above 2^53 it cannot represent every whole number, so a script cannot produce every `long`: the literal `9223372036854775807` is 2^63 in Luau, which a `long` cannot hold. A whole number that Luau does represent is read exactly.
- Managed interop is documented for strings, numbers, booleans, tables, functions, coroutines, userdata, and buffers. Vector values are not documented as a managed interop surface yet.
- Async managed callbacks are available through `CreateFunctionManual(...)` and the `LuauAwaiter` of its `LuauArgs`. Userdata methods and the `Call` metamethod can await in the same way; getters, setters and the other metamethods cannot. `CreateFunction(...)` delegates, generated `[LuauModule]` functions, and generated `[LuauUserdata]` methods can return `Task` or `ValueTask`.
- An awaiting callback only suspends coroutines that the host drives with `ExecuteAsync(...)`, `InvokeAsync(...)`, or `ResumeAsync(...)`, and only where Luau can yield: not inside a metamethod, a `table.sort` comparator, or a sync `Invoke(...)` made from another callback. Anywhere else it fails with a Luau error before its work starts. Coroutines that scripts create and resume themselves get that error too; there is no scheduler for them.
- A delegate that was created from an `async` lambda and is typed `Action` runs as `async void`. The generator rejects that where it can see the lambda, also inside a conditional expression; a delegate held in a variable is not checked.
- `CreateFunction(...)` callbacks take at most 16 parameters and none by reference. A task they return must not be nullable.
- Generated exports reject explicit interface implementations, partial methods without an implementation, and nullable task returns. An `init` accessor is not exported as a setter.
- `ExecuteAsync(...)` and `InvokeAsync(...)` create a new coroutine for every call.
- Only the token of an async host call stops a running script. `Execute(...)`, `Invoke(...)`, and `Resume(...)` take no token, and there is no instruction budget. Use the async methods where a script must be stoppable.
- An exception that the host's `ILuauFileSystem` throws while Luau resolves a `require(...)` path is reported as a module that was not found. Only an exception from reading the module file carries its message into the Luau error.

## Known edges

These are deliberate. The library does not guard against them, so your code has to.

### Errors

- An exception from a synchronous callback becomes a Luau error, whatever its type. A script can catch it with `pcall(...)`, including `OperationCanceledException` and `OutOfMemoryException`. Only cancellation of an async host call by its own token ends the script.
- When Luau itself runs out of memory outside a script call, for example while the host creates a table, the process does not recover.
- Deep recursion between scripts and callbacks is stopped by Luau's call depth limit. The error text is wrapped once per level on the way out, so it can get long.
- A Luau string is a sequence of bytes. Reading it as `string` decodes UTF-8 and replaces invalid sequences, so two different Luau strings can become the same managed string. Read bytes when the content is not text.
- `LuaException` carries the error as text. Error values that are not strings are tracked in [#35](https://github.com/rosslight/Darp.Luau/issues/35).

### Async and threading

- A cancelled token stops a script at Luau's safepoints only. Work without one, such as `string.rep` with a large count, and a running managed callback are not stopped. See [What is not stopped](../features/coroutines.md#what-is-not-stopped).
- A call that fails with a script error after its token was cancelled is reported as cancelled, not as that error: stopping a script can itself surface as an error, which cannot be told from one the script made.
- Disposing the state does not complete an async host call whose awaited work never finishes. The call fails with `ObjectDisposedException` when the work completes, so cancel the work.
- The result of one task belongs to one callback. A second callback that awaits the same `Task<LuauReturn>` fails.
- Awaiting methods of one userdata instance can interleave: while one waits, a script can call another. Guard state that must not be seen half-changed.
- A turn must not block on a lock that code holds across an `await` of the same state: the turn that would release it cannot start.
- Turns are not sliced and the queue of a state has no limit. A callback that keeps posting work keeps the state busy.
- A host dispatcher that pumps messages inside a turn can start another turn of the same state inside it.
- Code you post to `SynchronizationContext.Current` from a callback runs in a later turn. An exception it throws is unhandled, like on any synchronization context. `Send` is not supported.

### Ownership

- A `LuauReturn` holds the values you gave it until it is returned. One that you build and then drop keeps them until the state is disposed.
- Await every async host call and dispose the `LuauValue`s of `ExecuteMultiAsync(...)` and `InvokeMultiAsync(...)`. A result nobody reads keeps its references until the state is disposed.
- Owned wrappers such as `LuauTable` are structs. A copy is the same reference: disposing one copy disposes them all.

### Userdata

- For a binary operator such as `a + b`, Luau uses the metamethod of the left operand, and that of the right one only when the left has none. It does not try the other one after an error.
- The static side of a userdata type holds the value a static property had when a state first asked for the type table. Later changes of the property do not reach scripts.
- A managed object keeps the userdata type it was first pushed as. A class derived from a userdata type is not a userdata type of its own.
- A script can call a method on any instance of its type, for example `a.add(b, 1)`. Do not rely on the instance the method was read from.

### Sandbox

- A sandbox makes the globals and the tables directly in them read-only, not what those tables contain. Nested tables, the members of a userdata and the table a module returned stay writable.
- A script in an environment does not get the faster calls of built-in functions that a sandboxed state gives a script with globals of its own.
- A sandbox is not a boundary for scripts the host does not trust and sets no limit on memory or running time.

### Modules and generated code

- `require(...)` reads whatever path the `ILuauFileSystem` of the state resolves, including paths above the entry script. Restrict the file system if scripts must stay in one directory.
- `[LuauMember]` members of a base class are not exported. Declare them on the type that carries `[LuauModule]` or `[LuauUserdata]`.

## What this means in practice

- If you want file-based script loading, use `LoadFile(path)` for entry scripts.
- If you want file-backed modules, call `EnableScriptModules()` and execute the entry script with `LoadFile(path)`, which assigns the required `@...` chunk name automatically.
- If you want callback signatures outside the supported `CreateFunction(...)` subset, use `CreateFunctionManual(...)`.
- Start with source-generated modules and userdata for fixed host APIs. Use manual `RegisterModule(...)`, `CreateFunctionManual(...)`, or `ILuauUserdata<T>` for shapes the generated model cannot express.
- If you need more than the current typed `Invoke(...)` or chunk execution overload set, either compose around `InvokeMulti(...)` or `ExecuteMulti()`, call a returned function explicitly, or add an explicit overload.
- If you need long-lived access to callback values, promote borrowed `*View` values to owned references before the callback returns.

## Expect change

These boundaries are not promises that the library will stay narrow forever. They are the parts that are documented and supported today.
