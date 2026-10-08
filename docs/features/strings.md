# Strings

Strings are Luau's UTF-8 text values. In Darp.Luau you usually see four shapes:

- `string`: managed copy with normal .NET lifetime.
- `ReadOnlySpan<byte>`: borrowed UTF-8 bytes that alias Luau memory.
- `LuauString`: owned Luau string reference that you can keep and dispose.
- `LuauStringView`: borrowed callback-scoped string view.

If the data is arbitrary bytes rather than text, see [Buffers](buffers.md).

## Choose a representation

| Shape | Ownership | Common APIs | Use when |
| --- | --- | --- | --- |
| `string` | managed copy | passing `string` into Luau, `GetUtf8String(...)`, `TryReadUtf8String(..., out string?)`, `CreateFunction((string x) => ...)` | normal text interop |
| `ReadOnlySpan<byte>` | borrowed UTF-8 bytes | `TryGetUtf8String(...)`, `TryReadUtf8String(...)`, `LuauString.TryGet(...)` | immediate inspection without allocating |
| `LuauString` | owned Luau reference | `CreateString(...)`, `GetLuauString(...)` | keeping or reusing the same Luau string value |
| `LuauStringView` | borrowed callback view | `TryReadLuauString(...)` | callback code that stays inside the current frame |

The main choice is whether you want managed text or the Luau value itself.

## Create and push strings

Most code just passes `string`:

```csharp
lua.Globals.Set("name", "Ada");

using LuauFunction greet = lua.Globals.GetLuauFunction("greet");
greet.Invoke<LuauNil>("Ada");
```

Create `LuauString` only when you want to keep or reuse the same Luau string value:

```csharp
using LuauString name = lua.CreateString("Ada");
lua.Globals.Set("nameRef", name);
```

- Passing `string` encodes managed text into a Luau string.
- Passing `LuauString` reuses an existing Luau-backed value and works anywhere an API accepts `IntoLuau`.

## Read strings from tables and globals

```csharp
string name = lua.Globals.GetUtf8String("name");

using LuauString owned = lua.Globals.GetLuauString("name");
if (owned.TryGet(out ReadOnlySpan<byte> utf8))
{
    Console.WriteLine(System.Text.Encoding.UTF8.GetString(utf8));
}
```

- `GetUtf8String(...)` and `TryGetUtf8String(..., out string?)` return managed copies.
- `GetLuauString(...)` and `TryGetLuauString(...)` return owned wrappers that need disposal. Their spans stay valid until you dispose the wrapper.
- `GetUtf8StringOrNil(...)` and `TryGetUtf8StringOrNil(...)` handle the usual missing-key-or-`nil` case.
- `LuauMarshal.TryGetUtf8StringSpan(table, key, out ReadOnlySpan<byte>)` reads the bytes in place, without a copy and without a wrapper. Nothing keeps that memory alive; see [Lifetimes](../concepts/lifetimes.md#borrowed-spans-are-still-borrowed).

If you only need text, use the managed copy. Use `GetLuauString(...)` when you need the Luau string as a first-class value or want its bytes without copying.

## Read strings in callbacks

For fixed signatures, let `CreateFunction(...)` decode to `string`:

```csharp
using LuauFunction shout = lua.CreateFunction((string text) => text.ToUpperInvariant());
lua.Globals.Set("shout", shout);
```

For manual callbacks, read exactly the shape you want:

```csharp
using LuauFunction measure = lua.CreateFunctionBuilder(static args =>
{
    if (!args.TryReadUtf8String(1, out ReadOnlySpan<byte> text, out string? error))
        return LuauReturn.Error(error);

    return LuauReturn.Ok(text.Length);
});
```

- `CreateFunction((string text) => ...)` is the shortest fixed-signature form.
- `TryReadUtf8String(..., out ReadOnlySpan<byte>, ...)` borrows UTF-8 bytes.
- `TryReadUtf8String(..., out string?, ...)` copies into managed text.
- `TryReadLuauString(...)` returns a `LuauStringView` when you need the Luau value itself.

## Lifetime rules

- `string` is a managed copy with normal .NET lifetime.
- `ReadOnlySpan<byte>` aliases Luau memory, so consume it immediately.
- `LuauStringView` is valid only during the current callback frame. Call `ToOwned()` before storing it or crossing an async boundary.
- `LuauString` owns the Luau value and must be disposed, but any span you read from it is still borrowed.

The same promotion rule applies to the other callback `*View` types. For the broader ownership model, see [Lifetimes and ownership](../concepts/lifetimes.md).

## Guidance

- Use `string` by default.
- Use `ReadOnlySpan<byte>` for fast, immediate UTF-8 inspection.
- Use `LuauString` when you want to keep, pass around, or return the same Luau string value.
- Use `LuauStringView` only inside the current callback frame, and call `ToOwned()` before keeping it.
- Use [Buffers](buffers.md) when the data is arbitrary bytes rather than text.
