# Getting started

Darp.Luau is a .NET wrapper around [Luau](https://luau.org/) focused on native AOT compatibility, typed value access, and explicit ownership for Luau-backed references.

!!! info

    Darp.Luau is still under active development. Expect breaking API changes while the library surface settles.
    See [Limitations and current boundaries](concepts/limitations.md) for the parts that are intentionally narrow today.

This documentation is organized around the way you use the library in practice:

- create a `LuauState` and choose built-in libraries,
- run Luau source with `Load(...).Execute(...)`,
- load host modules and file-backed script modules through `require(...)`,
- move values between Luau and C# through strings, tables, functions, buffers, and userdata,
- understand which values are owned references and which values are borrowed callback views.

## Concepts

- `LuauState` owns the underlying Luau VM.
- Owned wrappers such as `LuauTable` and `LuauFunction` can outlive the current call frame, but they need disposal.
- Borrowed `*View` types such as `LuauTableView` and `LuauFunctionView` are callback-scoped.
- `CreateFunction(...)` is the normal typed callback API, but it must be called directly so the generator can intercept it.
- `[LuauModule]` and `[LuauUserdata]` are the recommended source-generated paths for exposing fixed host APIs.
- `RegisterModule(...)`, `CreateFunctionBuilder(...)`, and manual `ILuauUserData<T>` implementations are the manual APIs for shapes a generated type cannot express.

See [Concepts](concepts/index.md).

## Add the package

```bash
dotnet add package Darp.Luau
```

## Create a state

`LuauState` is the main entry point. It creates the Luau VM, loads the selected built-in libraries, and exposes the global table.

```csharp
using Darp.Luau;

using var lua = new LuauState();
```

The default constructor loads all standard libraries. Pass `LuauLibraries.None` to create a state without standard libraries.

## Chunks

`Load(...).Execute()` runs Luau source from managed code:

```csharp
lua.Load(
    """
    function add(a, b)
      return a + b
    end
    """
).Execute();

using LuauFunction add = lua.Globals.GetLuauFunction("add");
double result = add.Invoke<double>(1, 2);
```

Chunks can also return values directly to managed code. Use `Load(...).Execute<TR>()` for one typed value, `Load(...).Execute<TR1, TR2>()`, ... for typed multi-return calls, and `Load(...).ExecuteMulti()` for raw `LuauValue[]` access:

```csharp
(int total, int delta) = lua.Load("return 20, 4").Execute<int, int>();
```

If a chunk should keep its own globals, create an environment table with `CreateEnvironment()` and pass it through `WithEnvironment(...)`.

If you want file-based execution, use `LoadFile(path)`. It names the chunk after the script path, so if that script should be able to call file-backed `require(...)`, you only need to call `EnableScriptModules()` first.

See [Modules and require](features/modules.md), and [Chunks](features/chunks.md) for the full chunk execution API, return behavior, and ownership notes.

## Move data with tables

```csharp
using LuauTable config = lua.CreateTable();
config.Set("name", "Ada");
config.Set("score", 42);
config.Set("enabled", true);

lua.Globals.Set("config", config);

using LuauTable roundTripped = lua.Globals.GetLuauTable("config");
string name = roundTripped.GetUtf8String("name");
double score = roundTripped.GetNumber("score");
bool? enabled = roundTripped.GetBooleanOrNil("enabled");
```

See [Tables](features/tables.md) for `Get*`, `TryGet*`, `*OrNil`, dense-array helpers, and raw Luau wrappers.

## Work with strings

```csharp
lua.Globals.Set("name", "Ada");

string roundTripped = lua.Globals.GetUtf8String("name");
```

Use `string` for managed text, `ReadOnlySpan<byte>` for immediate borrowed UTF-8 bytes, `LuauString` for owned Luau-backed string values, and `LuauStringView` inside callbacks. See [Strings](features/strings.md).

## Work with buffers

```csharp
byte[] payload = [0x01, 0x02, 0x03];

lua.Globals.Set("payload", payload);

byte[] roundTripped = lua.Globals.GetBuffer("payload");
```

Use `byte[]` for managed copies, `ReadOnlySpan<byte>` for immediate borrowed reads, `LuauBuffer` for owned Luau-backed values, and `LuauBufferView` inside callbacks. See [Buffers](features/buffers.md).

## Expose a callback

Register managed callbacks with `CreateFunction(...)` and store the resulting `LuauFunction` in globals or tables:

```csharp
using LuauFunction log = lua.CreateFunction((string message) => Console.WriteLine(message));
lua.Globals.Set("log", log);

using LuauFunction pair = lua.CreateFunction((int a, int b) => (a + b, a - b));
lua.Globals.Set("pair", pair);

lua.Load("""log("hello from luau")""").Execute();
```

Use `CreateFunction(...)` for supported fixed signatures, including managed userdata parameters and returns for generated `[LuauUserdata]` or manual `ILuauUserData<TSelf>` types and supported top-level tuple returns. If you need manual argument parsing, unsupported callback shapes, or custom error shaping, use `CreateFunctionBuilder(...)`. See [Functions](features/functions.md).

## Expose userdata

Use `[LuauUserdata]` to generate the normal userdata hook implementation for a partial class:

```csharp
[LuauUserdata]
public sealed partial class Player
{
    [LuauMember("name", Access = LuauPropertyAccess.ReadOnly)]
    public required string Name { get; init; }

    [LuauMember("score")]
    public int Score { get; set; }
}
```

Then expose instances as managed userdata:

```csharp
var player = new Player { Name = "Ada", Score = 42 };

lua.Globals.Set("player", IntoLuau.FromUserdata(player));

lua.Load(
    """
    currentName = player.name
    player.score = 100
    """
).Execute();
```

See [Userdata](features/userdata.md) for generated userdata, manual hook behavior, retrieval APIs, identity rules, and lifetimes.

## Register a host module

Use `[LuauModule]` to generate the normal registration path for fixed host APIs:

```csharp
[LuauModule("game")]
public static partial class GameModule
{
    [LuauMember("answer")]
    public static int Answer => 42;

    [LuauMember("add")]
    public static int Add(int left, int right) => left + right;
}

lua.RegisterModule(GameModule.ModuleName, GameModule.OnLoad);
```

Generated registration registers a module that Luau loads through `require("game")`. Use manual `RegisterModule(...)` only when you need dynamic table construction or unsupported callback shapes:

```csharp
lua.RegisterModule("game", static (state, in LuauTable module) =>
{
    module.Set("answer", 42);

    using LuauFunction add = state.CreateFunction((int a, int b) => a + b);
    module.Set("add", add);
});

lua.Load(
    """
    local game = require("game")
    result = game.add(game.answer, 8)
    """
).Execute();
```

See [Modules and require](features/modules.md) for generated modules, manual `RegisterModule(...)`, file-backed script modules, nested paths, and current generator boundaries.

## Where to next

- [Chunks](features/chunks.md)
- [Lifetimes and ownership](concepts/lifetimes.md)
- [Type mapping](concepts/type-mapping.md)
- [Functions](features/functions.md)
- [Coroutines](features/coroutines.md)
- [Strings](features/strings.md)
- [Buffers](features/buffers.md)
- [Tables](features/tables.md)
- [Userdata](features/userdata.md)
- [Standard libraries](features/standard-libraries.md)
- [Modules and require](features/modules.md)
