# Sandbox

A sandboxed state lets scripts use everything the host set up, but not change it. Its globals and standard libraries are read-only, and every script gets globals of its own. This is Luau's own sandbox model.

Set up the state first, then enable the sandbox. It cannot be switched off again:

```csharp
using var lua = new LuauState(LuauLibraries.Minimal | LuauLibraries.Math | LuauLibraries.String);
using LuauFunction log = lua.CreateFunction((string message) => Console.WriteLine(message));
lua.Globals.Set("log", log);
lua.RegisterModule<GameModule>();
lua.EnableSandbox();

int damage = lua.Load("local base = 10; log('hit'); return math.floor(base * 1.5)").Execute<int>();
```

`IsSandboxed` tells whether a state is sandboxed.

## What scripts can no longer do

Each of these raises a Luau error and leaves the state usable:

```lua
math.floor = function() return 0 end   -- attempt to modify a readonly table
string.format = nil
getmetatable("").__index = {}          -- the metatable of strings is read-only too
_G.log = nil                           -- globals of the host as well
setfenv(0, {})                         -- getfenv and setfenv are removed
```

Read-only are the global table, every table directly in it (the standard libraries and tables of the host), and the metatable of strings.

## Every script has globals of its own

A script that assigns a global keeps it to itself. The assignment does not reach the globals of the state, and the next script does not see it:

```csharp
lua.Load("hits = 0; function onHit() hits = hits + 1; return hits end; return onHit()").Execute<int>(); // 1
lua.Load("return hits == nil and onHit == nil").Execute<bool>();                                      // true
```

A script reads every other name from the globals of the state. `_G` is that shared table, so `_G.name = value` fails while `name = value` works.

A script can still give a name another value for itself: after `log = print`, that script calls `print`, and every other script still calls the `log` of the host.

## Read a script's globals from the host

The host does not get at the globals a script made for itself. There are two ways to get something back:

- Let the script return it: `return { onTick = onTick }`. This is the fast way; see below.
- Give the script an environment and read from that:

```csharp
using LuauTable enemy = lua.CreateEnvironment();
lua.Load("hits = 0; function onTick(dt) hits = hits + 1; return hits end").WithEnvironment(enemy).Execute();

using LuauFunction onTick = enemy.GetLuauFunction("onTick");
int hits = onTick.Invoke<int>(0.016);
```

An environment works in a sandboxed state as in any other: several scripts can share it, and the host can read and write it. See [Chunks](chunks.md#use-an-environment).

## Speed

In a sandboxed state Luau knows that the standard libraries are what they were when the script was loaded. A script that runs with globals of its own, and every [script module](modules.md#file-backed-script-modules), then calls built-in functions such as `math.floor` directly and resolves a name like `string.format` once when it is loaded. Code that spends its time in built-in functions and loops over tables runs several times faster this way.

A script in an environment does not get this. The host can change an environment at any time, so its scripts look everything up, as they do in a state that is not sandboxed.

What the host put into the globals keeps working as before:

- A userdata, or a table that can still change, is read every time a script uses it.
- A built-in function that the host replaced or removed before enabling the sandbox is called by what its name holds now. A library that was not loaded stays unavailable.

A function that was loaded before `EnableSandbox()` keeps working and keeps looking everything up. Enable the sandbox before loading scripts.

Whatever ran before could also keep something the sandbox takes away. `getfenv` and `setfenv` are removed from the globals, not revoked: a reference a script stored in a table beforehand still works. Treat everything that runs before `EnableSandbox()` as part of the host's setup.

## What the host can no longer do

The globals are read-only for the host as well:

```csharp
lua.Globals.Set("late", 1);                    // LuaException
lua.LoadStandardLibraries(LuauLibraries.Os);   // InvalidOperationException
lua.EnableScriptModules();                     // InvalidOperationException, unless require was set up before
```

Everything that does not change the globals still works: registering another host module once one was registered or script modules were enabled, creating functions, tables, userdata and environments.

## What it is not

- Only the tables directly in the globals are read-only. A script can still write to `config.limits.max`, to the members of a userdata, and to the table a module returned, which every script that requires the module shares.
- It is not a boundary for scripts you do not trust, and it sets no limit on memory or running time. Run such scripts in a state of their own that only has what they may use. A script can be stopped through the cancellation token of an async call; see [Coroutines](coroutines.md).
