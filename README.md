# Darp.Luau

[![Darp.Results](https://img.shields.io/nuget/v/Darp.Luau.svg)](https://www.nuget.org/packages/Darp.Luau)
[![Downloads](https://img.shields.io/nuget/dt/Darp.Luau)](https://www.nuget.org/packages/Darp.Luau)
![License](https://img.shields.io/github/license/rosslight/darp-luau)
![.NET](https://img.shields.io/badge/version-.NET10-blue)

`Darp.Luau` is a .NET wrapper around [Luau](https://luau.org/) focused on native AOT compatibility, typed value access, and explicit ownership for Luau-backed references.

## Why another lua library

- NativeAOT first
- Typed reads and writes for tables, functions, userdata, strings, and buffers
- Clear lifetime guarantees both stability and performance
- Simple API through source-generated interceptors
- Host modules and managed userdata
- Coroutines and async managed callbacks
- Support for `linux`,`windows`,`macos` on both `x64`,`arm64`

## Quick start

```csharp
using Darp.Luau;

using var lua = new LuauState();

using LuauFunction log = lua.CreateFunction((string message) => Console.WriteLine(message));
lua.Globals.Set("log", log);

using LuauTable config = lua.CreateTable();
config.Set("name", "Ada");
config.Set("enabled", true);
lua.Globals.Set("config", config);

lua.Load(
    """
    function add(a, b)
      return a + b
    end

    log(config.name)
    result = add(20, 22)
    """
).Execute();

double result = lua.Globals.GetNumber("result");
```

## Call Lua functions from C#

```csharp
using LuauFunction add = lua.Globals.GetLuauFunction("add");
double sum = add.Invoke<double>(1, 2);

using LuauFunction pair = lua.Globals.GetLuauFunction("pair");
(int total, int delta) = pair.Invoke<int, int>(20, 4);
```

`Invoke<TR>(...)` converts a single Luau return value to the managed type you ask for and ignores extras. Use `Invoke<TR1, TR2>(...)`, ... for typed multi-return calls, and `InvokeMulti(...)` for raw `LuauValue[]` access. The current argument buffer accepts up to 4 arguments per call.

`Load(...).Execute(...)` follows the same return-shaping pattern for chunk execution: use `Load(...).Execute<TR>()` for the first typed return value, `Load(...).Execute<TR1, TR2>()`, ... for typed multi-return calls, and `Load(...).ExecuteMulti()` for raw `LuauValue[]` access.

```csharp
(int total, int delta) = lua.Load("return 20, 4").Execute<int, int>();
```

## Expose managed callbacks

Use `CreateFunction(...)` for supported fixed delegate signatures:

```csharp
using LuauFunction sum = lua.CreateFunction((int a, int b) => a + b);
lua.Globals.Set("sum", sum);

using LuauFunction pair = lua.CreateFunction((int a, int b) => (a + b, a - b));
lua.Globals.Set("pair", pair);
```

Use `CreateFunctionBuilder(...)` when you need manual argument parsing, explicit user-facing errors, or a callback shape that the generator-backed path does not support:

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
```

`CreateFunction(...)` must be called directly at the call site so the generator can intercept it. It supports fixed delegate signatures, including supported top-level tuple returns. If you need a shape that is not supported there, use `CreateFunctionBuilder(...)`.

A callback can return a `Task` or `ValueTask`. Run the script with `ExecuteAsync(...)` or `InvokeAsync(...)`, and it waits for the result without blocking a thread:

```csharp
using LuauFunction fetch = lua.CreateFunction(
    (string url, CancellationToken cancellationToken) => httpClient.GetStringAsync(url, cancellationToken)
);
lua.Globals.Set("fetch", fetch);

int length = await lua.Load("return #fetch('https://example.com')").ExecuteAsync<int>([], cancellationToken);
```

For the script, `fetch(url)` is a normal call. A `CancellationToken` parameter is not a Luau argument; it receives the token of the async call.

Methods of `[LuauUserdata]` types and functions of `[LuauModule]` types await in the same way. Builder callbacks return `LuauReturn.Await(...)`.

## Work with tables

```csharp
using LuauTable settings = lua.CreateTable();
settings.Set("volume", 0.8);
settings.Set("muted", false);
settings.Set("blob", new byte[] { 1, 2, 3 });

lua.Globals.Set("settings", settings);

using LuauTable roundTripped = lua.Globals.GetLuauTable("settings");
double volume = roundTripped.GetNumber("volume");
bool muted = roundTripped.GetBoolean("muted");
byte[] blob = roundTripped.GetBuffer("blob");
```

Use `Get*` for required values, `TryGet*` for optional or external data, and `*OrNil` when `nil` is part of the contract.

## Work with userdata

```csharp
[LuauUserdata]
public sealed partial class Player
{
    [LuauMember("name", Access = LuauPropertyAccess.ReadOnly)]
    public required string Name { get; init; }

    [LuauMember("save")]
    public Task SaveAsync(CancellationToken cancellationToken) =>
        File.WriteAllTextAsync("player.txt", Name, cancellationToken);
}

var player = new Player { Name = "Ada" };

lua.Globals.Set("player", IntoLuau.FromUserdata(player));
await lua.Load("player:save()").ExecuteAsync();

Player samePlayer = lua.Globals.GetUserdata<Player>("player");

using LuauUserdata playerRef = lua.Globals.GetLuauUserdata("player");
_ = playerRef.TryGetManaged(out Player? resolvedPlayer, out string? error);
```

Prefer `[LuauUserdata]` for regular script-facing properties and methods. Use a manual `ILuauUserData<T>` implementation only when you need custom dispatch or behavior the generator cannot express. See [Userdata](docs/features/userdata.md) for the full hook model.

`CreateFunction(...)` also supports managed userdata parameters and returns for generated `[LuauUserdata]` types and manual `ILuauUserData<TSelf>` implementations.

## Register host modules

Prefer `[LuauModule]` for regular fixed host APIs:

```csharp
[LuauModule("game")]
public static partial class GameModule
{
    [LuauMember("answer")]
    public static int Answer => 42;

    [LuauMember("add")]
    public static int Add(int left, int right) => left + right;

    [LuauMember("wait")]
    public static Task Wait(double seconds, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
}

lua.RegisterModule(GameModule.ModuleName, GameModule.OnLoad);
```

Use manual `RegisterModule(...)` when you need dynamic table construction or unsupported callback shapes:

```csharp
lua.RegisterModule("game", static (state, in LuauTable module) =>
{
    module.Set("answer", 42);

    using LuauFunction add = state.CreateFunction((int a, int b) => a + b);
    module.Set("add", add);
});
```

Host modules are loaded from Luau with `require("game")`. Generated and manual modules share the same require context as file-backed script modules.

## Ownership and lifetime

- `LuauTable`, `LuauFunction`, `LuauCoroutine`, `LuauString`, `LuauBuffer`, `LuauUserdata`, and reference-backed `LuauValue` are owned references and should be disposed.
- `LuauTableView`, `LuauFunctionView`, `LuauCoroutineView`, `LuauStringView`, `LuauBufferView`, `LuauUserdataView`, and `LuauArgs` are borrowed callback-scoped values. Read them before a callback awaits.
- Reference-backed values belong to one `LuauState`; cross-state usage is invalid.

## Benchmarks

![Mean time and managed memory allocated per operation of Darp.Luau, NuLua, NLua and Lua-CSharp](https://raw.githubusercontent.com/rosslight/Darp.Luau/benchmark-results/libraries.svg)

Compared with:

- [NuLua](https://github.com/nuskey8/NuLua) (Unified Lua5.x/LuaJIT/Luau bindings for .NET and Unity)
- [NLua](https://github.com/NLua/NLua) (dynamic bridge between Lua world and the .NET)
- [Lua-CSharp](https://github.com/nuskey8/Lua-CSharp) (High performance Lua interpreter implemented in C# for .NET and Unity)

The benchmarks cover synchronous workloads and async invocation, including managed callbacks that complete immediately or await `Task.Yield()`. Darp.Luau and Lua-CSharp run all async scenarios; NuLua runs async invocation and completed callbacks. NLua is not measured in the async scenarios. See [how to run the benchmarks and the NuLua yielding limitation](benchmarks/README.md).

Latest run on a GitHub-hosted runner; the chart names the versions that were measured. See the [full results](https://github.com/rosslight/Darp.Luau/tree/benchmark-results) and the [benchmark code](https://github.com/rosslight/Darp.Luau/tree/main/benchmarks).

## Current boundaries

- `Load(...).Execute(...)` is the script execution API today. Use `LoadFile(path)` to load an entry script from disk.
- `CreateFunction(...)` is generator-backed and has no runtime fallback.
- `LuauState` is not thread-safe. Continuations of async callbacks run one at a time on the thread pool, or on a host dispatcher passed to the constructor. Use the synchronous API only when no async call is outstanding or from inside a callback.
- Async managed callbacks need an async host call (`ExecuteAsync`, `InvokeAsync`, `ResumeAsync`).

## Roadmap

Darp.Luau is pre-1.0 and breaking changes are still expected. The aim before 1.0 is breadth: basic support for every larger Luau concept before completing any single one.

Planned before 1.0:

- A basic set of userdata metamethods
- Interrupting a running script
- An opt-in sandbox with read-only libraries and globals
- Structured errors with script location, traceback, and the original managed exception
- Luau type definitions generated for the host API
- More than four arguments and return values, including variadics
- Script modules from sources other than disk
- Consistent naming, such as `LuauException` and `ILuauUserdata<T>`

Later:

- Metatables on plain tables from the host
- Vectors as host values
- Memory limits, garbage-collector control, and isolation for untrusted scripts
- Compiler options, precompiled bytecode, and native code generation
- Converting C# objects to and from tables

## Non-goals

- A thread-safe `LuauState`. The host is responsible for using a state from one thread at a time.
- Sharing Luau-backed references between states.
- Target frameworks older than `net10.0`. Going forward, only .NET versions that are in service are supported.
- Game-engine runtimes such as Unity or Godot.
- Debugger and profiler hooks.
- Type-checking scripts from the host. Use the generated type definitions with editor tooling instead.
