# Userdata

Userdata lets Luau hold onto a real managed object instead of flattening it into a table.

Use it when the script should interact with identity, mutable state, or host-defined behavior.

Typical examples include game entities, domain objects, service handles, and other values that should round-trip back to the same managed instance.

## The userdata surfaces

In Darp.Luau, userdata usually shows up in four forms:

- `[LuauUserdata]` generates the normal script-facing behavior for your managed type.
- `ILuauUserData<T>` is the manual hook surface used when generation is not enough.
- `LuauUserdata` is an owned userdata reference that you can keep and dispose.
- `LuauUserdataView` is a borrowed callback-scoped userdata view.

That split matters:

- use the managed type when you want direct access to your own object,
- use `LuauUserdata` when you want an owned Luau wrapper,
- use `LuauUserdataView` only inside the current callback frame, or promote it with `ToOwned()` first.

## Generate userdata with `[LuauUserdata]`

The recommended way to expose a managed type as userdata is to mark a partial class with `[LuauUserdata]` and mark the script-facing members with `[LuauMember]`.

```csharp
using Darp.Luau;

[LuauUserdata]
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

The source generator emits the `ILuauUserData<Player>` implementation for `OnIndex`, `OnSetIndex`, and `OnMethodCall`.

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

For method calls, Luau uses the userdata method-call syntax `player:add(1)`. The generated method receives only the declared managed parameters; `self` is handled by the userdata dispatch layer.

### Generated property access

`LuauPropertyAccess.Auto` is the default:

- getter and setter -> read-write,
- getter only -> read-only,
- setter only -> write-only.

Use `Access = LuauPropertyAccess.ReadOnly`, `WriteOnly`, or `ReadWrite` when the Luau contract should be stricter than the managed property shape.

Generated read-only properties return an error when Luau tries to assign them. Generated write-only properties return an error when Luau tries to read them.

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

### Generated userdata rules

Generated userdata supports:

- instance properties with supported stored value types,
- instance methods with fixed supported signatures,
- methods that return `Task` or `ValueTask`, and `CancellationToken` parameters,
- generated or manual managed userdata as supported property, parameter, and return types,
- generated userdata types as `CreateFunction(...)` parameters and returns.

The generator reports diagnostics for unsupported shapes instead of emitting weak runtime fallbacks. Current boundaries include:

- exported userdata types must be partial, top-level, non-generic classes,
- fields are not exported,
- static userdata members are not supported,
- member names are single-segment only; dotted paths are for generated modules,
- optional, `params`, `ref`, `in`, `out`, generic methods, and by-ref returns are not supported,
- generated and manual userdata hooks cannot be mixed on the same type.

Use manual `ILuauUserData<T>` only when you need custom dispatch, dynamic member names, custom validation, unsupported member shapes, or unusual error behavior.

## Implement userdata manually with `ILuauUserData<T>`

Implement the static members on `ILuauUserData<T>` when the generated userdata model cannot express what Luau can do with your object:

| Hook | Luau syntax | Receives | Unknown member behavior |
| --- | --- | --- | --- |
| `OnIndex` | `player.name` | `self`, `LuauState`, member name | return `LuauReturnSingle.NotHandled` and Luau sees `nil` |
| `OnSetIndex` | `player.score = 10` | `self`, `LuauArgsSingle`, member name | return `LuauOutcome.NotHandledError` and Luau gets an unknown-member error |
| `OnMethodCall` | `player:add(1)` | `self`, `LuauArgs`, method name | return `LuauReturn.NotHandledError` and Luau gets an unknown-method error |

These hooks are manual callback surfaces, closer to `CreateFunctionBuilder(...)` than to `CreateFunction(...)`: you read arguments yourself and return `LuauReturn*` or `LuauOutcome` values explicitly.

For `player:add(1)`, `self` is already passed separately, so `functionArgs` contains only the actual method arguments.

```csharp
internal sealed class PlayerUserdata : ILuauUserData<PlayerUserdata>
{
    public required string Name { get; init; }
    public int Score { get; private set; }

    public static LuauReturnSingle OnIndex(PlayerUserdata self, in LuauState state, in ReadOnlySpan<char> fieldName) =>
        fieldName switch
        {
            "name" => LuauReturnSingle.Ok(self.Name),
            "score" => LuauReturnSingle.Ok(self.Score),
            _ => LuauReturnSingle.NotHandled,
        };

    public static LuauOutcome OnSetIndex(PlayerUserdata self, LuauArgsSingle args, in ReadOnlySpan<char> fieldName)
    {
        switch (fieldName)
        {
            case "score":
                if (!args.TryReadNumber(out int score, out string? error))
                    return LuauOutcome.Error(error);

                self.Score = score;
                return LuauOutcome.Ok();
            default:
                return LuauOutcome.NotHandledError;
        }
    }

    public static LuauReturn OnMethodCall(
        PlayerUserdata self,
        LuauArgs functionArgs,
        in ReadOnlySpan<char> methodName
    )
    {
        switch (methodName)
        {
            case "add":
                if (!functionArgs.TryValidateArgumentCount(1, out string? error))
                    return LuauReturn.Error(error);
                if (functionArgs.ArgumentCount != 1)
                    return LuauReturn.Error("Expected exactly 1 argument.");
                if (!functionArgs.TryReadNumber(1, out int amount, out error))
                    return LuauReturn.Error(error);

                self.Score += amount;
                return LuauReturn.Ok(self.Score);
            default:
                return LuauReturn.NotHandledError;
        }
    }

    public static implicit operator IntoLuau(PlayerUserdata value) => IntoLuau.FromUserdata(value);
}
```

### Async methods

`OnMethodCall` can finish later through the awaiter of its `LuauArgs`, like a callback built with `CreateFunctionBuilder(...)`. The script waits at `player:save()` without blocking a thread:

```csharp
case "save":
    if (!functionArgs.TryGetAwaiter(out LuauAwaiter awaiter, out string? error))
        return LuauReturn.Error(error);
    return awaiter.Await(
        new ValueTask(
            File.WriteAllTextAsync($"{self.Name}.txt", self.Score.ToString(), functionArgs.CancellationToken)
        )
    );
```

The same rules apply: run the script with `ExecuteAsync(...)`, `InvokeAsync(...)`, or `ResumeAsync(...)`, and read every argument before you return. `OnIndex` and `OnSetIndex` cannot await. See [Coroutines](coroutines.md#async-managed-callbacks).

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

Userdata hooks participate in normal Luau error handling:

- return `LuauReturn.Error(...)` or `LuauOutcome.Error(...)` for expected user-facing failures,
- return `NotHandled` or `NotHandledError` for unknown members,
- let exceptions bubble only for truly exceptional failures.

Thrown exceptions become Luau errors too, including inside `pcall(...)`.

Methods can return zero, one, or many values through `LuauReturn.Ok(...)`.

## Identity and lifetime

Managed userdata keeps object identity:

- pushing the same managed instance into the same `LuauState` again reuses the same Lua userdata identity while that userdata is still alive,
- pushing two different managed instances creates two different Lua userdata values even if their contents match.

Lifetime rules follow the normal owned-vs-borrowed model:

- `LuauUserdata` is an owned reference; keep it in a `using` block.
- `LuauUserdataView` is callback-scoped and temporary.
- `LuauArgs`, `LuauArgsSingle`, and other `*View` values in userdata hooks are also callback-scoped.
- call `ToOwned()` before storing or reusing a borrowed userdata value outside the current callback.

See [Lifetimes and ownership](../concepts/lifetimes.md) for the broader ownership model.

## Design guidance

- Expose a small, stable script-facing surface instead of mirroring your full managed type.
- Prefer generated `[LuauUserdata]` declarations for regular property and method surfaces.
- Use manual `ILuauUserData<T>` when you need behavior the generator cannot express.
- Keep validation and error messages intentional inside the hooks.
- Prefer tables for plain data and userdata for identity or behavior.
