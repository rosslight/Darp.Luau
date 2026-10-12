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

A table value is read with four generic methods. Pick the one that matches how strict the table contract is:

| API | Missing key | Another type | Returns | Best for |
| --- | --- | --- | --- | --- |
| `Get<int>("score")`, `Get<string>("name")` | throws | throws | managed value | required fields |
| `Get<int?>("score")`, `GetOrNull<string>("name")` | `null` | throws | nullable managed value | absent or `nil` is valid |
| `TryGet("score", out int score)`, `TryGet("name", out string? name)` | `false` | `false` | managed value | optional or external data |
| `TryGet("score", out int? score)`, `TryGetOrNull("name", out string? name)` | `true` with `null` | `false` | nullable managed value | optional fields with validation |
| `Get<LuauTable>("nested")` | throws | throws | owned `LuauTable` | nested table access |

`T` is a number type, an enum, `bool`, `string`, `byte[]`, a managed userdata class, an owned wrapper such as `LuauTable` or `LuauFunction`, or `LuauValue`.

A value type says through its nullable form that `nil` is fine: `Get<int?>` and `TryGet(..., out int? score)`. A reference type such as `string`, `byte[]` or a managed userdata class uses `GetOrNull<T>` and `TryGetOrNull<T>` instead, because `string` and `string?` are the same type at run time.

```csharp
using LuauTable config = lua.Globals.Get<LuauTable>("config");

string name = config.Get<string>("name");

if (config.TryGet("score", out int score))
{
    // use score
}

bool? enabled = config.Get<bool?>("enabled");
```

Normal table lookup returns `nil` for both missing keys and keys explicitly set to `nil`, so every read treats both cases the same.

A failed `Get<T>` throws `LuaGetException` with a message such as `table value: number expected, got string`.

Reads use `__index` like a script does. When the metamethod raises an error, `Get<T>` and `GetOrNull<T>` throw `LuaGetException` with the error of the script as its message, and `TryGet<T>` and `TryGetOrNull<T>` return `false`.

`Get<string>` and `Get<byte[]>` copy strings and buffers into managed memory. To read them in place, use `LuauMarshal.TryGetUtf8StringSpan(...)`, `LuauMarshal.TryGetBufferSpan(...)`, or their `OrNil` variants, whose `isNil` tells you whether the lookup resolved to `nil`. See [Lifetimes](../concepts/lifetimes.md#borrowed-spans-are-still-borrowed) for what that requires of you.

## Raw and nested values

When you want an owned reference instead of an immediate managed copy, read the value as its owned wrapper:

```csharp
using LuauTable root = lua.Globals.Get<LuauTable>("config");
using LuauTable graphics = root.Get<LuauTable>("graphics");
using LuauFunction save = root.Get<LuauFunction>("save");
```

`Get<T>` and `TryGet<T>` return an owned reference when `T` is `LuauTable`, `LuauFunction`, `LuauString`, `LuauBuffer`, `LuauUserdata`, or `LuauCoroutine`. The caller disposes it, so keep it in a `using` block.

For string-specific guidance around managed text, borrowed UTF-8 bytes, and `LuauString`, see [Strings](strings.md).

For the buffer-specific API split between `byte[]`, `ReadOnlySpan<byte>`, `LuauBuffer`, and `LuauBufferView`, see [Buffers](buffers.md).

For fully dynamic code, read a `LuauValue`:

- `table[key]` returns a `LuauValue`; missing keys come back as `LuauValueType.Nil`.
- `Get<LuauValue>(key)` also returns a nil value for a missing key. It throws `LuaGetException` when `__index` raises an error.
- `TryGet(key, out LuauValue value)` returns `true` with a nil value for a missing key, and `false` when `__index` raises an error.

If the `LuauValue` may be reference-backed, dispose it when you are done with it.

A typed read such as `TryGet("score", out int score)` usually makes your API contract clearer than reading a `LuauValue` and converting it afterwards.

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

`LuauTableView` shows up in callbacks, where a table argument is read as one. It has the same API as `LuauTable`: `Get<T>`, `GetOrNull<T>`, `TryGet<T>`, `TryGetOrNull<T>`, the indexer, `Set`, `ContainsKey`, and `ListCount`. A callback reads and writes a table argument through the view:

```csharp
using LuauFunction readValue = lua.CreateFunctionManual(static args =>
{
    LuauTableView table = args[1].Get<LuauTableView>();

    return LuauReturn.Ok(table.Get<double>("value"));
});
```

`LuauTableView` does not own a registry reference. It is valid only while the current callback frame is active. Call `ToOwned()` in two cases:

- to keep the table: before storing it or using it after the callback returns,
- to enumerate the table with `foreach` or `IPairs()`, which only `LuauTable` offers.

`ToOwned()` returns an owned reference, which the caller disposes.

## Lifetime notes

- `Get<string>(...)` and `Get<byte[]>(...)` return managed copies.
- `LuauMarshal` exposes Luau-owned memory of a table value. Consume it immediately and copy it if you need a longer lifetime.
- Enumerating a table with `foreach` hands you an owned key and value per step. Dispose both when they may be reference-backed. The table may change while you enumerate it; like in Luau, entries added on the way may or may not be visited.
- Owned wrappers returned by a read such as `Get<LuauTable>(...)` need disposal.
- `LuauTableView` follows the same callback-scoped lifetime rules as the other `*View` types.

## String representation

`ToString()` on a table, function, userdata, or coroutine returns its type and address, such as `table: 0x000001c2a4f0e8d0`. It runs no script code, so it ignores `__tostring`. Call the global `tostring` through `Invoke(...)` when you want what a script would see.

## Guidance

Use tables for script-facing configuration and state, but keep your higher-level managed API more structured than your raw Luau table layout. That usually gives you better validation, versioning, and error messages.
