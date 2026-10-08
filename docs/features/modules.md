# Modules and require

`require(...)` is the Luau-side module loading surface in Darp.Luau. It can load host-provided C# modules registered with `RegisterModule(...)`, and file-backed script modules after `EnableScriptModules()`.

Host modules and file-backed script modules share one require context. The context resolves module names, loads modules lazily, and caches successful results for later `require(...)` calls.

## Host modules

The recommended way to expose a fixed host API is to mark a partial type with `[LuauModule]` and mark the script-facing members with `[LuauMember]`.

```csharp
using Darp.Luau;

[LuauModule("game")]
public static partial class GameModule
{
    [LuauMember("answer")]
    public static int Answer => 42;

    [LuauMember("add")]
    public static int Add(int left, int right) => left + right;
}
```

The source generator emits `ModuleName` and `OnLoad(...)` members. Register them once for each state that should receive the module:

```csharp
using var lua = new LuauState();

lua.RegisterModule(GameModule.ModuleName, GameModule.OnLoad);

lua.Load(
    """
    local game = require("game")
    result = game.add(game.answer, 8)
    """
).Execute();
```

The registration stores a host module factory. The module table is created lazily when Luau first calls `require("game")`, then cached for later calls.

### Static and instance modules

Static module types generate a static `OnLoad(...)` method. Instance module types implement `ILuauModule<TModule>`, which is useful when the module needs managed state:

```csharp
[LuauModule("scoreboard")]
public sealed partial class ScoreboardModule
{
    private readonly List<string> _names = [];

    [LuauMember("add")]
    public void Add(string name) => _names.Add(name);

    [LuauMember("count")]
    public int Count => _names.Count;
}

var scoreboard = new ScoreboardModule();
lua.RegisterModule<ScoreboardModule>(scoreboard);
```

Generated module properties are snapshot values written to the module table when it is loaded. Instance module properties are not supported because they would need live table accessors.

### Nested module paths

Module member names can contain dots to create nested tables:

```csharp
[LuauModule("workshop")]
public sealed partial class WorkshopModule
{
    [LuauMember("tools.hammer")]
    public int MakeHammer(int size) => size * 2;
}
```

Luau sees this as:

```lua
local workshop = require("workshop")
local bundle = workshop.tools.hammer(5)
```

### Generated module member rules

Generated modules support:

- methods with fixed supported signatures,
- methods that return `Task` or `ValueTask`, and `CancellationToken` parameters, as described in [Generated async methods](userdata.md#generated-async-methods),
- static read-only properties,
- instance methods on class modules,
- generated or manual managed userdata as supported parameter and return types.

The generator reports diagnostics for unsupported shapes instead of emitting weak runtime fallbacks. Current boundaries include:

- exported module types must be partial, top-level, and non-generic,
- instance structs are not supported,
- fields are not exported,
- instance properties are not supported,
- write-capable module properties are not supported,
- optional, `params`, `ref`, `in`, `out`, generic methods, and by-ref returns are not supported,
- member paths cannot conflict, such as using both `Field` and `Field.u8` as exports.

Use stable Luau-facing names in `[LuauMember("...")]` instead of mirroring managed names by default.

## Manual host modules

Use manual `RegisterModule(...)` when the generated module model cannot express the shape you need, such as dynamic table construction, custom validation, or unsupported callback signatures.

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

Populate the module inside the registration callback. The temporary `module` wrapper passed to `RegisterModule(...)` is only meant for that setup step.

Strongly typed manual modules can implement `ILuauModule<TModule>`:

```csharp
public sealed class GameModule : ILuauModule<GameModule>
{
    public static string ModuleName => "game";

    public void OnLoad(LuauState lua, in LuauTable module)
    {
        module.Set("answer", 42);
    }
}

lua.RegisterModule<GameModule>(new GameModule());
```

## File-backed script modules

Call `EnableScriptModules()` when Luau scripts should load other `.luau` or `.lua` files from disk:

```csharp
using var lua = new LuauState();
lua.EnableScriptModules();

string path = Path.GetFullPath("scripts/main.luau");
lua.Load(File.ReadAllBytes(path)).WithName("@" + path).Execute();
```

File-backed script module paths must start with `./`, `../`, or `@alias`:

```lua
local x = require("./x")
local parent = require("../parent")
local aliased = require("@shared/tools")
```

File-backed script modules need a recognized requiring chunk name:

- `=stdin` for managed source that acts like a stdin entrypoint,
- `@<absolute-or-relative-path>` for source that should resolve modules from a file location.

For file-backed entry scripts, pass a chunk name that starts with `@` and points at the script path.

## Shared require behavior

`RegisterModule(...)`:

- throws if the host module name is already registered,
- reserves `./`, `../`, `/`, `\`, and `@` prefixes for script-module paths,
- installs the shared require context if it was not installed already,
- loads the module lazily on the first matching `require(...)`,
- caches the module table after a successful load.

`EnableScriptModules()` adds file-backed script resolution to the same require context:

- relative paths resolve from the requiring file,
- module lookup checks `.luau`, `.lua`, `init.luau`, and `init.lua`,
- `.luaurc` and `.config.luau` aliases are supported,
- module results are cached by absolute file path.

Each file-backed module must return exactly one value, and file-backed modules cannot yield while loading. Returned values can be any Luau value that Darp.Luau can surface.

If file-backed module loading fails, Darp.Luau reports the detailed loader message through the normal Luau error path. Managed callers receive it through `LuaException`; Luau code can catch it with `pcall(...)`.
