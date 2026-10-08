# Tables

Tables are Luau's general-purpose container type. In Darp.Luau you usually work with them in three forms:

- `LuauTable`: an owned table reference that you can keep and dispose.
- `LuauTableView`: a borrowed callback-scoped table view.
- dense-array helpers such as `CreateTable(ReadOnlySpan<double>)`, `ListCount`, and `IPairs()`.

## Create and populate

```csharp
using LuauTable config = lua.CreateTable();
config.Set("name", "Ada");
config.Set("score", 42);
config.Set("enabled", true);

lua.Globals.Set("config", config);
```

`Set` accepts any `IntoLuau` key and value, so strings, numbers, buffers, tables, functions, userdata, and other Luau wrappers all work.

`LuauState.Globals` is just another `LuauTable`, so the same patterns apply there too.

`Set` writes like a script does, so `__newindex` runs for a new key. It throws `LuaException` when Luau rejects the write: the metamethod raises an error, the table is frozen with `table.freeze(...)`, or the key is `nil` or NaN. A `null` key throws `ArgumentNullException`.

## Choose a read API

Pick the read family that matches how strict the table contract is:

| API | Missing key | Wrong type | Returns | Best for |
| --- | --- | --- | --- | --- |
| `GetNumber("score")` | throws | throws | managed value | required fields |
| `TryGetNumber("score", out int score)` | `false` | `false` | managed value | optional or external data |
| `GetNumberOrNil("score")` | `null` | throws | nullable managed value | absent or `nil` is valid |
| `TryGetNumberOrNil("score", out int? score)` | `true` with `null` | `false` | nullable managed value | optional fields with validation |
| `GetLuauTable("nested")` | throws | throws | owned `LuauTable` | nested table access |
| `TryGetLuauTable("nested", out LuauTable nested)` | `false` | `false` | owned `LuauTable` | nested table access without exceptions |

The same split exists across booleans, strings, buffers, functions, userdata, and the other `GetLuau*` wrapper APIs.

```csharp
using LuauTable config = lua.Globals.GetLuauTable("config");

string name = config.GetUtf8String("name");

if (config.TryGetNumber("score", out int score))
{
    // use score
}

bool? enabled = config.GetBooleanOrNil("enabled");
```

Normal table lookup returns `nil` for both missing keys and keys explicitly set to `nil`, so the `*OrNil` methods treat both cases the same.

Reads use `__index` like a script does. When the metamethod raises an error, `Get*` throws and `TryGet*` returns `false`; the overloads with an `out string? error` carry the message.

The table getters copy strings and buffers into managed memory. To read them in place, use `LuauMarshal.TryGetUtf8StringSpan(...)`, `LuauMarshal.TryGetBufferSpan(...)`, or their `OrNil` variants, whose `isNil` tells you whether the lookup resolved to `nil`. See [Lifetimes](../concepts/lifetimes.md#borrowed-spans-are-still-borrowed) for what that requires of you.

## Raw and nested values

When you want another Luau wrapper instead of an immediate managed copy, use `GetLuau*` or `TryGetLuau*`:

```csharp
using LuauTable root = lua.Globals.GetLuauTable("config");
using LuauTable graphics = root.GetLuauTable("graphics");
using LuauFunction save = root.GetLuauFunction("save");
```

These methods return owned references. Keep them in `using` blocks like any other `LuauTable`, `LuauFunction`, `LuauString`, `LuauBuffer`, or `LuauUserdata`.

For string-specific guidance around managed text, borrowed UTF-8 bytes, and `GetLuauString(...)`, see [Strings](strings.md).

For the buffer-specific API split between `byte[]`, `ReadOnlySpan<byte>`, `LuauBuffer`, and `LuauBufferView`, see [Buffers](buffers.md).

For fully dynamic code, use one of these raw-value options:

- `table[key]` returns a `LuauValue`; missing keys come back as `LuauValueType.Nil`.
- `GetLuauValue(...)` returns a non-`nil` `LuauValue` or throws.
- `TryGetLuauValue(...)` returns `false` when the resolved value is `nil`.

If the `LuauValue` may be reference-backed, dispose it when you are done with it.

The `TryGet<T>(...)` table extension is a convenient wrapper over raw `LuauValue` conversion, but the explicit `Get*` and `TryGet*` methods usually make your API contract clearer.

## Presence checks

`ContainsKey` tells you whether normal table lookup resolves to a non-`nil` value:

```csharp
if (config.ContainsKey("save"))
{
    // includes values provided through __index
}
```

This is metamethod-aware. A `__index` lookup can make a key appear present, and a key whose resolved value is `nil` counts as missing. `ContainsKey` throws `LuaException` when `__index` raises an error.

## Dense arrays and list-like tables

If a table is really a dense 1-based array, the list helpers are convenient:

```csharp
using LuauTable values = lua.CreateTable([1, 4, 9]);

int count = values.ListCount;

foreach ((int index, double value) in values.IPairs<double>())
{
    Console.WriteLine($"{index}: {value}");
}
```

`CreateTable(ReadOnlySpan<double>)` writes numeric keys starting at `1`.

`IPairs()` returns raw `LuauValue` entries, while `IPairs<T>()` converts each value to `T` and stops at the first `nil` or type mismatch.

`ListCount`, `IPairs()`, and `IPairs<T>()` are only reliable for dense arrays. Sparse tables and tables with holes can shorten enumeration and make the reported length misleading.

You can also enumerate the whole table with `foreach`, but key order is not guaranteed and the key/value entries are `LuauValue`s that may need disposal.

## Borrowed table views

`LuauTableView` shows up in callback APIs such as `CreateFunctionBuilder(...)` and `LuauArgs.TryReadLuauTable(...)`:

```csharp
using LuauFunction readValue = lua.CreateFunctionBuilder(static args =>
{
    if (!args.TryReadLuauTable(1, out LuauTableView table, out string? error))
        return LuauReturn.Error(error);

    using LuauTable owned = table.ToOwned();
    return LuauReturn.Ok(owned.GetNumber("value"));
});
```

`LuauTableView` does not own a registry reference. It is valid only while the current callback frame is active, so call `ToOwned()` before storing it or using it after the callback returns.

## Lifetime notes

- `GetUtf8String(...)` and `GetBuffer(...)` return managed copies.
- `LuauMarshal` exposes Luau-owned memory of a table value. Consume it immediately and copy it if you need a longer lifetime.
- Enumerating a table with `foreach` hands you an owned key and value per step. Dispose both when they may be reference-backed. The table may change while you enumerate it; like in Luau, entries added on the way may or may not be visited.
- Owned wrappers returned by `GetLuau*` need disposal.
- `LuauTableView` follows the same callback-scoped lifetime rules as the other `*View` types.

## String representation

`ToString()` on a table, function, userdata, or coroutine returns its type and address, such as `table: 0x000001c2a4f0e8d0`. It runs no script code, so it ignores `__tostring`. Call the global `tostring` through `Invoke(...)` when you want what a script would see.

## Guidance

Use tables for script-facing configuration and state, but keep your higher-level managed API more structured than your raw Luau table layout. That usually gives you better validation, versioning, and error messages.
