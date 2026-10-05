# Chunks

`LuauState.Load(...)` creates a `LuauChunk`, a lightweight wrapper around source text plus chunk-specific execution options.

That chunk object is the execution surface:

- it carries the source to compile,
- it can carry a chunk name via `WithName(...)`,
- it can carry an execution environment via `WithEnvironment(...)`,
- it executes the source through `Execute(...)`,
- it can expose all returns through `ExecuteMulti()`,
- it can be compiled into a reusable `LuauFunction` with `ToFunction()`.

`Load(...)` accepts both `ReadOnlySpan<char>` and `ReadOnlySpan<byte>` source. Use the `char` overload for normal .NET text and the `byte` overload when you already have UTF-8 bytes.

## Create a chunk

```csharp
LuauChunk chunk = lua.Load(
    """
    function add(a, b)
      return a + b
    end

    result = add(20, 22)
    """
);
```

`LuauChunk` is intentionally ephemeral.

- it is a `ref struct`, so it stays stack-bound,
- it does not cache compiled code,
- each execution recompiles and reloads the chunk,
- if you want reuse, convert it to a `LuauFunction`.

## Execute a chunk

Use `Execute()` when you only care about side effects:

```csharp
lua.Load("result = 42").Execute();

double result = lua.Globals.GetNumber("result");
```

Chunk execution can also accept arguments through `...`:

```csharp
int result = lua.Load(
    """
    local a, b = ...
    return a + b
    """
).Execute<int>(20, 22);
```

## Read typed returns

Typed execution mirrors `LuauFunction.Invoke(...)`.

```csharp
int answer = lua.Load("return 42").Execute<int>();

(int total, int delta) = lua.Load("return 20, 4").Execute<int, int>();
```

Typed chunk returns follow the same conversion rules as other typed APIs.

- If the chunk returns more values than requested, extra values are ignored.
- If the chunk returns fewer values than requested, missing values are read as `nil` and may fail conversion.
- If a return value cannot be converted to the requested managed type, the call throws `InvalidCastException`.

## Read all returns dynamically

Use `ExecuteMulti()` when you want every return value without fixing the shape up front:

```csharp
LuauValue[] values = lua.Load("return 10, 'hello', true").ExecuteMulti();

using LuauValue numberValue = values[0];
using LuauValue textValue = values[1];
using LuauValue flagValue = values[2];

numberValue.TryGet(out int number, acceptNil: false);
textValue.TryGet(out string? text, acceptNil: false);
flagValue.TryGet(out bool flag, acceptNil: false);
```

Each `LuauValue` may own a registry reference. Dispose returned values when they may be reference-backed. See [Lifetimes and ownership](../concepts/lifetimes.md).

## Name a chunk

Use `WithName(...)` when the chunk should carry a meaningful source name, such as a file-backed entrypoint for `require(...)` or a clearer script name in errors.

```csharp
string path = Path.GetFullPath("scripts/main.luau");

lua.Load(File.ReadAllBytes(path))
    .WithName("@" + path)
    .Execute();
```

This is especially important for `EnableScriptModules()`, because entry chunk names drive path resolution.

## Use an environment

Use `CreateEnvironment()` when a chunk should keep its own globals while still reading from the state's shared globals:

```csharp
using LuauTable env = lua.CreateEnvironment();

int result = lua.Load(
    """
    count = (count or 0) + 1
    return math.max(count, 1)
    """
).WithEnvironment(env).Execute<int>();
```

Environment reads fall back to `lua.Globals`, but assignments stay on the environment table itself.

- Reuse the same environment across multiple chunk executions when they should share chunk-local globals.
- `_G` inside that environment points back to the environment table.
- This is a scoping helper, not a sandbox or isolation boundary.

## Convert a chunk into a function

If the same source should run multiple times, convert the chunk into a reusable `LuauFunction`:

```csharp
using LuauFunction function = lua.Load(
    """
    local a, b = ...
    return a + b
    """
).ToFunction();

int result = function.Invoke<int>(20, 22);
```

This is the reusable path. `LuauChunk` itself does not retain compiled state.

## Current boundaries

- `LoadFile(path)` creates a chunk from a script on disk and names it after the file path. There is no separate `DoFile(...)` helper.
- `ToFunction()` is the explicit reusable path; plain chunk execution recompiles on each call.

## Error behavior

- Luau load and runtime failures throw `LuaException`.
- Typed execution throws when a requested return value cannot be read or converted.
