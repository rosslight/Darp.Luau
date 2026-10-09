# Userdata

Userdata lets Luau hold onto a real managed object instead of flattening it into a table.

Use it when the script should interact with identity, mutable state, or host-defined behavior.

Typical examples include game entities, domain objects, service handles, and other values that should round-trip back to the same managed instance.

## The userdata surfaces

In Darp.Luau, userdata usually shows up in four forms:

- `[LuauUserdata]` generates the normal script-facing behavior for your managed type.
- `ILuauUserdata<T>` is implemented by hand when generation is not enough.
- `LuauUserdata` is an owned userdata reference that you can keep and dispose.
- `LuauUserdataView` is a borrowed callback-scoped userdata view.

That split matters:

- use the managed type when you want direct access to your own object,
- use `LuauUserdata` when you want an owned Luau wrapper,
- use `LuauUserdataView` only inside the current callback frame, or promote it with `ToOwned()` first.

A type is described either with attributes or by hand, never with both.

## Generate userdata with `[LuauUserdata]`

The recommended way to expose a managed type as userdata is to mark a partial class with `[LuauUserdata]` and mark the script-facing members with `[LuauMember]`.

```csharp
using Darp.Luau;

[LuauUserdata("Player")]
public sealed partial class Player
{
    [LuauMember("name", Access = LuauPropertyAccess.ReadOnly)]
    public required string Name { get; init; }

    [LuauMember("score")]
    public int Score { get; set; }

    [LuauMember("add")]
    public int Add(int amount)
    {
        Score += amount;
        return Score;
    }
}
```

The source generator implements `ILuauUserdata<Player>` for the type.

The name in `[LuauUserdata("Player")]` is what `typeof(player)` returns in Luau, and Luau uses it in its own error messages. It must be a Luau identifier that is not the name of a built-in type such as `number`.

Expose instances with the normal managed-userdata APIs:

```csharp
var player = new Player { Name = "Ada", Score = 40 };

lua.Globals.Set("player", IntoLuau.FromUserdata(player));

lua.Load(
    """
    player.score = 41
    result = player:add(1)
    currentName = player.name
    """
).Execute();
```

### Methods are values

A method is a function that takes the instance as its first argument. `player:add(1)` and `player.add(player, 1)` are the same call, and a script can keep the function:

```lua
local add = player.add
add(player, 1)
if player.save then player:save() end
```

The generated method receives only the declared managed parameters; the instance is handled for you. A call without an instance of the type, such as `player.add(1)`, is an error that names the method.

### Generated property access

`LuauPropertyAccess.Auto` is the default:

- getter and setter -> read-write,
- getter only -> read-only,
- setter only -> write-only.

Use `Access = LuauPropertyAccess.ReadOnly`, `WriteOnly`, or `ReadWrite` when the Luau contract should be stricter than the managed property shape.

Assigning a read-only member, reading a write-only member, and assigning a method are errors. Reading a name the type does not declare gives `nil`; assigning one is an error.

### Generated async methods

A method that returns `Task`, `Task<T>`, `ValueTask`, or `ValueTask<T>` suspends the script until it completes. Luau receives the awaited result:

```csharp
[LuauMember("save")]
public async Task<bool> SaveAsync(string slot, CancellationToken cancellationToken)
{
    await File.WriteAllTextAsync($"{slot}.txt", Score.ToString(), cancellationToken);
    return true;
}
```

```lua
local saved = player:save("slot1")
```

A `CancellationToken` parameter is not a Luau argument. It receives the token of the async host call.

Run the script with `ExecuteAsync(...)`, `InvokeAsync(...)`, or `ResumeAsync(...)`. Where the script cannot be suspended, the method is not called and the script receives a Luau error. `async void` methods are rejected. See [Coroutines](coroutines.md#async-managed-callbacks) for errors, cancellation, and threading.

### Metamethods

Mark a method or an operator with `[LuauMetamethod]` to let scripts use an operator on the type, call it, or index it with keys that are not members:

```csharp
[LuauUserdata("Vec2")]
public sealed partial class Vec2(double x, double y)
{
    [LuauMember("x")]
    public double X { get; } = x;

    [LuauMember("y")]
    public double Y { get; } = y;

    [LuauMetamethod(LuauMetamethod.Add)]
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);

    // An instance method: the instance is the left operand, as in vec * 2.
    [LuauMetamethod(LuauMetamethod.Mul)]
    private Vec2 Scale(double factor) => new(X * factor, Y * factor);

    // A static method names both operands, so the instance can be the right one: 2 * vec.
    [LuauMetamethod(LuauMetamethod.Mul)]
    private static Vec2 Scale(double factor, Vec2 vec) => vec.Scale(factor);

    [LuauMetamethod(LuauMetamethod.ToString)]
    private string Describe() => $"({X}, {Y})";
}
```

The method can have any name and accessibility. The generator only checks that its operands and its result fit the metamethod. An operator can only declare the metamethod it stands for: `+`, `-`, `*`, `/`, `%`, unary `-`, `==`, `<` and `<=`. For every other metamethod, such as `Pow`, mark a method:

| Metamethod | Luau | Operands | Result |
| --- | --- | --- | --- |
| `Add`, `Sub`, `Mul`, `Div`, `Mod`, `Pow`, `IDiv` | `a + b`, `a - b`, `a * b`, `a / b`, `a % b`, `a ^ b`, `a // b` | two, at least one of them the type | one value |
| `Concat` | `a .. b` | two, at least one of them the type | one value |
| `Unm` | `-a` | the instance | one value |
| `Eq`, `Lt`, `Le` | `a == b`, `a < b`, `a <= b` | two values of the type | `bool` |
| `Len` | `#a` | the instance | a number |
| `ToString` | `tostring(a)` | the instance | a `string` that is never null |
| `Index` | `a[key]` for a key that is not a member | the instance and the key | one value |
| `NewIndex` | `a[key] = value` for a key that is not a member | the instance, the key and the value | nothing |
| `Call` | `a(...)` | the instance and the arguments | as a `[LuauMember]` method |

An instance method takes the instance as its first operand. A static method lists every operand.

Several methods can declare the same metamethod when Luau can tell their operands apart by type, or for `Call` by their number. An `int` and a `double` are both a number in Luau, so two overloads that only differ in that are rejected. The overload is chosen before an operand is read.

What follows from how Luau works:

- For `a + b`, Luau uses the metamethod of `a`, and that of `b` only when `a` has none. It never tries the other one after an error. An overload on type `B` for a left operand of type `A` is therefore only reached when `A` does not declare the operator.
- `a == b` only calls `Eq` for two different instances of the same type. Two values of different types are never equal. `a < b` across types is an error.
- An operator the type does not declare raises Luau's own error, such as `attempt to perform arithmetic (div) on Vec2 and number`. An operator with operands that no overload takes raises the same kind of error.
- `Index` and `NewIndex` are only reached for keys that are not declared members, also for keys that are not strings. An `Index` whose overloads do not take the key gives `nil`.
- Only `Call` can return a task. Luau cannot suspend a script inside another metamethod.

### The static side

Static members marked with `[LuauMember]` belong to the type, not to an instance:

```csharp
[LuauUserdata("Vec2")]
public sealed partial class Vec2(double x, double y)
{
    [LuauMember("new")]
    public static Vec2 Create(double x, double y) => new(x, y);

    [LuauMember("zero")]
    public static Vec2 Zero { get; } = new(0, 0);
}
```

`GetTypeTable<T>()` returns them as a table, which you put where scripts should find it:

```csharp
using LuauTable vec2 = lua.GetTypeTable<Vec2>();
lua.Globals.Set("Vec2", vec2);
```

```lua
local v = Vec2.new(1, 2) + Vec2.zero
```

To offer the type through a module, put the table into the module when it loads:

```csharp
lua.RegisterModule("geometry", static (LuauState state, in LuauTable module) =>
{
    using LuauTable vec2 = state.GetTypeTable<Vec2>();
    module.Set("Vec2", vec2);
});
```

The table is read-only, and every script of a state shares it. A static property is read once for each state, when the table is first asked for; it must be read-only. The static side and the instances do not see each other's members: `Vec2.new` exists, `v.new` does not.

### Generated userdata rules

Generated userdata supports:

- instance properties with supported stored value types,
- instance methods with fixed supported signatures,
- methods that return `Task` or `ValueTask`, and `CancellationToken` parameters,
- metamethods on methods and operators,
- static methods and read-only static properties, as the static side of the type,
- generated or manual managed userdata as supported property, parameter, and return types,
- generated userdata types as `CreateFunction(...)` parameters and returns.

The generator reports diagnostics for unsupported shapes instead of emitting weak runtime fallbacks. Current boundaries include:

- exported userdata types must be partial, top-level, non-generic classes,
- fields are not exported,
- member names are single-segment only; dotted paths are for generated modules,
- optional, `params`, `ref`, `in`, `out`, generic methods, and by-ref returns are not supported,
- a type with `[LuauUserdata]` cannot also implement `Register` by hand.

Implement `ILuauUserdata<T>` by hand only when you need member names that are only known at run time, custom validation, unsupported member shapes, or unusual error behavior.

## Implement userdata by hand with `ILuauUserdata<T>`

A type that implements `ILuauUserdata<T>` describes itself in a static `Register` method:

```csharp
internal sealed class PlayerUserdata : ILuauUserdata<PlayerUserdata>
{
    public required string Name { get; init; }
    public int Score { get; private set; }

    public static void Register(LuauUserdataRegistry<PlayerUserdata> registry)
    {
        registry.TypeName = "Player";
        registry.AddGetter("name", static (self, _) => LuauReturnSingle.Ok(self.Name));
        registry.AddGetter("score", static (self, _) => LuauReturnSingle.Ok(self.Score));
        registry.AddSetter("score", static (self, value) =>
        {
            if (!value.TryReadNumber(out int score, out string? error))
                return LuauOutcome.Error(error);

            self.Score = score;
            return LuauOutcome.Ok();
        });
        registry.AddMethod("add", static (self, args) =>
        {
            if (args.ArgumentCount != 1)
                return LuauReturn.Error("Expected exactly 1 argument.");
            if (!args.TryReadNumber(1, out int amount, out string? error))
                return LuauReturn.Error(error);

            self.Score += amount;
            return LuauReturn.Ok(self.Score);
        });
    }

    public static implicit operator IntoLuau(PlayerUserdata value) => IntoLuau.FromUserdata(value);
}
```

| Registry method | Luau syntax | The callback receives |
| --- | --- | --- |
| `AddGetter` | `player.name` | the instance and the `LuauState` |
| `AddSetter` | `player.score = 10` | the instance and the assigned value as `LuauArgsSingle` |
| `AddMethod` | `player:add(1)`, `player.add(player, 1)` | the instance and the arguments, without the instance |
| `AddMetamethod` | operators, calls, unknown keys | the operands as Luau passes them |
| `AddFunction` | `Player.create(...)` on the [static side](#the-static-side) | the arguments |
| `AddValue` | `Player.limit` on the static side | the `LuauState`; called once for each state |

`TypeName` is optional here. Without it, `typeof(value)` is `userdata`.

The callbacks are manual callback surfaces, like `CreateFunctionManual(...)`: you read arguments yourself and return `LuauReturn*` or `LuauOutcome` values explicitly. `LuauArgs.State` gives a callback the state it runs in.

`Register` is called once per process, before the first instance reaches a state, and every state shares what it describes. It must therefore not capture a state. A name can have one getter and one setter, or be one method; declaring a name twice throws. An exception from `Register` is thrown again by every use of the type.

A metamethod receives its operands as Luau passes them, so for a binary operator the instance can be either one:

```csharp
registry.AddMetamethod(LuauMetamethod.Mul, static args =>
{
    if (args.IsUserdata<Vec2>(1) && args.GetValueType(2) is LuauValueType.Number)
    {
        // vec * 2
    }
    if (args.GetValueType(1) is LuauValueType.Number && args.IsUserdata<Vec2>(2))
    {
        // 2 * vec
    }
    return LuauReturn.Error($"attempt to perform arithmetic (mul) on {args.GetTypeName(1)} and {args.GetTypeName(2)}");
});
```

`GetValueType`, `IsUserdata<T>` and `GetTypeName` look at an argument without reading it.

### Member names that are only known at run time

`LuauMetamethod.Index` and `LuauMetamethod.NewIndex` receive every key that is not a declared member. Return a function from `Index` to serve a method name; like any method, it gets the instance as its first argument:

```csharp
registry.AddMetamethod(LuauMetamethod.Index, static args =>
{
    if (!args.TryReadUtf8String(2, out string? name, out string? error))
        return LuauReturn.Error(error);

    LuauFunction method = args.State.CreateFunctionManual(call => LuauReturn.Ok($"called {name}"));
    return LuauReturn.Ok(method.DisposeAndToLuauValue());
});
```

Create such a function once and keep it when scripts call it often.

### Async methods

A method can finish later through the awaiter of its `LuauArgs`, like a callback built with `CreateFunctionManual(...)`. The script waits at `player:save()` without blocking a thread:

```csharp
registry.AddMethod("save", static (self, args) =>
{
    if (!args.TryGetAwaiter(out LuauAwaiter awaiter, out string? error))
        return LuauReturn.Error(error);
    return awaiter.Await(
        new ValueTask(File.WriteAllTextAsync($"{self.Name}.txt", self.Score.ToString(), args.CancellationToken))
    );
});
```

The same rules apply: run the script with `ExecuteAsync(...)`, `InvokeAsync(...)`, or `ResumeAsync(...)`, and read every argument before you return. Getters, setters and metamethods other than `Call` cannot await. See [Coroutines](coroutines.md#async-managed-callbacks).

## Expose and retrieve userdata

```csharp
var player = new PlayerUserdata { Name = "Ada" };

lua.Globals.Set("player", player);

lua.Load(
    """
    player.score = 41
    result = player:add(1)
    """
).Execute();

PlayerUserdata samePlayer = lua.Globals.GetUserdata<PlayerUserdata>("player");

using LuauUserdata playerRef = lua.Globals.GetLuauUserdata("player");
_ = playerRef.TryGetManaged(out PlayerUserdata? resolvedPlayer, out string? error);
```

The implicit conversion operator is optional but convenient for manual userdata. If you do not define it on your managed type, use `IntoLuau.FromUserdata(player)` at the call site instead.

If you want to keep the Lua userdata wrapper itself, call `lua.GetOrCreateUserdata(player)` and hold the resulting `LuauUserdata` in a `using` block.

Choose the read API that matches what you need:

| Need | API |
| --- | --- |
| Resolve directly to a managed userdata instance | `GetUserdata<T>`, `TryGetUserdata<T>` |
| Accept missing or `nil` | `GetUserdataOrNil<T>`, `TryGetUserdataOrNil<T>` |
| Keep a generic owned userdata wrapper | `GetLuauUserdata`, `TryGetLuauUserdata` |
| Resolve an owned or borrowed userdata wrapper back to a managed instance | `LuauUserdata.TryGetManaged<T>`, `LuauUserdataView.TryGetManaged<T>` |

`GetUserdata<T>` and `TryGetManaged<T>` only succeed for managed userdata created by this library and matching `T`. Generic `LuauUserdata` wrappers can still represent other userdata values, but they will not resolve back to your managed type.

The same split exists inside callbacks:

- `args.TryReadUserdata<T>(...)` reads the managed instance directly.
- `args.TryReadLuauUserdata(...)` reads a borrowed `LuauUserdataView`.

## Error behavior

Userdata callbacks participate in normal Luau error handling:

- return `LuauReturn.Error(...)` or `LuauOutcome.Error(...)` for expected user-facing failures,
- let exceptions bubble only for truly exceptional failures.

Thrown exceptions become Luau errors too, including inside `pcall(...)`. The message names the member that threw.

Methods can return zero, one, or many values through `LuauReturn.Ok(...)`.

Scripts cannot get or replace the metatable of a userdata: `getmetatable(value)` returns the string `The metatable is locked`. Every value of a type shares one metatable, so a script that could change it would change them all.

## Identity and lifetime

Managed userdata keeps object identity:

- pushing the same managed instance into the same `LuauState` again reuses the same Lua userdata identity while that userdata is still alive,
- pushing two different managed instances creates two different Lua userdata values even if their contents match.

A userdata type is exactly the class that implements `ILuauUserdata<T>`. A managed object keeps the type it was first pushed as for as long as its userdata is alive.

Lifetime rules follow the normal owned-vs-borrowed model:

- `LuauUserdata` is an owned reference; keep it in a `using` block.
- `LuauUserdataView` is callback-scoped and temporary.
- `LuauArgs`, `LuauArgsSingle`, and other `*View` values in userdata callbacks are also callback-scoped.
- call `ToOwned()` before storing or reusing a borrowed userdata value outside the current callback.

See [Lifetimes and ownership](../concepts/lifetimes.md) for the broader ownership model.

## Design guidance

- Expose a small, stable script-facing surface instead of mirroring your full managed type.
- Prefer generated `[LuauUserdata]` declarations for regular property and method surfaces.
- Implement `ILuauUserdata<T>` by hand when you need behavior the generator cannot express.
- Keep validation and error messages intentional inside the callbacks.
- Prefer tables for plain data and userdata for identity or behavior.
