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
- Source-generated `[LuauUserdata]` types must be partial, top-level, non-generic classes. Fields, static exported members, dotted userdata member names, manual hook mixing, and unsupported method shapes are not generated.
- Generated exports currently emit runtime C# glue only. Luau type-file output is not a documented shipped feature yet.
- File-backed `require(...)` is available through `EnableScriptModules()`, but it requires explicit setup and a matching chunk-name convention for file entrypoints.
- `EnableScriptModules()` currently expects script modules to return exactly one value and not yield while loading.
- Managed interop is documented for strings, numbers, booleans, tables, functions, coroutines, userdata, and buffers. Vector values are not documented as a managed interop surface yet.
- Async managed callbacks are available through `CreateFunctionBuilder(...)` and the `LuauAwaiter` of its `LuauArgs`. Manual userdata methods can await through `OnMethodCall`; property reads and writes cannot. `CreateFunction(...)` delegates, generated `[LuauModule]` functions, and generated `[LuauUserdata]` methods can return `Task` or `ValueTask`.
- An awaiting callback only suspends coroutines that the host drives with `ExecuteAsync(...)`, `InvokeAsync(...)`, or `ResumeAsync(...)`, and only where Luau can yield: not inside a metamethod, a `table.sort` comparator, or a sync `Invoke(...)` made from another callback. Anywhere else it fails with a Luau error before its work starts. Coroutines that scripts create and resume themselves get that error too; there is no scheduler for them.
- A delegate that was created from an `async` lambda and is typed `Action` runs as `async void`. The generator rejects that where it can see the lambda; a delegate held in a variable is not checked.
- `ExecuteAsync(...)` and `InvokeAsync(...)` create a new coroutine for every call.

## What this means in practice

- If you want file-based script loading, use `LoadFile(path)` for entry scripts.
- If you want file-backed modules, call `EnableScriptModules()` and execute the entry script with `LoadFile(path)`, which assigns the required `@...` chunk name automatically.
- If you want callback signatures outside the supported `CreateFunction(...)` subset, use `CreateFunctionBuilder(...)`.
- Start with source-generated modules and userdata for fixed host APIs. Use manual `RegisterModule(...)`, `CreateFunctionBuilder(...)`, or `ILuauUserData<T>` for shapes the generated model cannot express.
- If you need more than the current typed `Invoke(...)` or chunk execution overload set, either compose around `InvokeMulti(...)` or `ExecuteMulti()`, call a returned function explicitly, or add an explicit overload.
- If you need long-lived access to callback values, promote borrowed `*View` values to owned references before the callback returns.

## Expect change

These boundaries are not promises that the library will stay narrow forever. They are the parts that are documented and supported today.
