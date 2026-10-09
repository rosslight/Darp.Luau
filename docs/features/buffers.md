# Buffers

Buffers hold arbitrary Luau bytes. In Darp.Luau you usually see four shapes:

- `byte[]`: managed copy with normal .NET lifetime.
- `ReadOnlySpan<byte>`: borrowed bytes that alias Luau memory.
- `LuauBuffer`: owned Luau buffer reference that you can keep and dispose.
- `LuauBufferView`: borrowed callback-scoped buffer view.

If the data is UTF-8 text, see [Strings](strings.md).

## Choose a representation

| Shape | Ownership | Common APIs | Use when |
| --- | --- | --- | --- |
| `byte[]` | managed copy | `GetBuffer(...)`, `TryReadBuffer(..., out byte[]?)`, passing `byte[]` into Luau | simple data transfer |
| `ReadOnlySpan<byte>` | borrowed bytes | `TryReadBuffer(...)`, `LuauBuffer.TryGet(...)`, `LuauMarshal.TryGetBufferSpan(...)` | immediate inspection without allocating |
| `LuauBuffer` | owned Luau reference | `CreateBuffer(...)`, `GetLuauBuffer(...)` | keeping or reusing the same Luau buffer value |
| `LuauBufferView` | borrowed callback view | `TryReadLuauBuffer(...)` | callback code that stays inside the current frame |

The main choice is whether you want a managed copy or the Luau value itself.

## Create and push buffers

Most code just passes `byte[]`:

```csharp
byte[] payload = [0x01, 0x02, 0x03];

lua.Globals.Set("payload", payload);

using LuauFunction send = lua.Globals.GetLuauFunction("send");
send.Invoke<LuauNil>(payload);
```

Create `LuauBuffer` only when you want to keep or reuse the same Luau buffer value:

```csharp
using LuauBuffer payload = lua.CreateBuffer([0x01, 0x02, 0x03]);
lua.Globals.Set("payloadRef", payload);
```

- Passing `byte[]` copies data into a Luau buffer.
- Passing `LuauBuffer` reuses an existing Luau-backed value and works anywhere an API accepts `IntoLuau`.

## Read buffers from tables and globals

```csharp
byte[] bytes = lua.Globals.GetBuffer("payload");

using LuauBuffer owned = lua.Globals.GetLuauBuffer("payload");
if (owned.TryGet(out ReadOnlySpan<byte> borrowed))
{
    Console.WriteLine(Convert.ToHexString(borrowed));
}
```

- `GetBuffer(...)` and `TryGetBuffer(..., out byte[]?)` copy into managed memory.
- `GetLuauBuffer(...)` and `TryGetLuauBuffer(...)` return owned wrappers that need disposal. Their spans stay valid until you dispose the wrapper.
- `LuauMarshal.TryGetBufferSpan(table, key, out ReadOnlySpan<byte>)` reads the bytes in place, without a copy and without a wrapper. Nothing keeps that memory alive; see [Lifetimes](../concepts/lifetimes.md#borrowed-spans-are-still-borrowed).
- `GetBufferOrNil(...)` and `TryGetBufferOrNil(...)` handle the usual missing-key-or-`nil` case.

If you only need bytes, use the copy or span APIs. Use `GetLuauBuffer(...)` when you need the Luau buffer as a first-class value.

## Read buffers in callbacks

For fixed signatures, accept borrowed bytes directly:

```csharp
static int Size(ReadOnlySpan<byte> bytes) => bytes.Length;

using LuauFunction size = lua.CreateFunction(Size);
lua.Globals.Set("size", size);
```

For manual callbacks, read exactly the shape you want:

```csharp
using LuauFunction measure = lua.CreateFunctionManual(static args =>
{
    if (!args.TryReadBuffer(1, out ReadOnlySpan<byte> bytes, out string? error))
        return LuauReturn.Error(error);

    return LuauReturn.Ok(bytes.Length);
});
```

- `CreateFunction(Size)` is the shortest fixed-signature form.
- `TryReadBuffer(..., out ReadOnlySpan<byte>, ...)` borrows bytes.
- `TryReadBuffer(..., out byte[]?, ...)` copies into managed memory.
- `TryReadLuauBuffer(...)` returns a `LuauBufferView` when you need the Luau value itself.

## Lifetime rules

- `byte[]` is a managed copy with normal .NET lifetime.
- `ReadOnlySpan<byte>` aliases Luau memory, so consume it immediately.
- `LuauBufferView` is valid only during the current callback frame. Call `ToOwned()` before storing it or crossing an async boundary.
- `LuauBuffer` owns the Luau value and must be disposed, but any span you read from it is still borrowed.

The same promotion rule applies to the other callback `*View` types. For the broader ownership model, see [Lifetimes and ownership](../concepts/lifetimes.md).

## Buffer library versus buffer values

`LuauLibraries.Buffer` enables Luau's script-side `buffer` library:

```csharp
using var lua = new LuauState(LuauLibraries.Buffer);
```

You do not need that flag for host-side buffer interop such as `CreateBuffer(...)`, passing `byte[]`, `GetBuffer(...)`, `GetLuauBuffer(...)`, `TryReadBuffer(...)`, or `TryReadLuauBuffer(...)`.

Enable the library only when your Luau code needs `buffer.*` helpers.

## Guidance

- Use `byte[]` by default.
- Use `ReadOnlySpan<byte>` for fast, immediate inspection.
- Use `LuauBuffer` when you want to keep, pass around, or return the same Luau buffer value.
- Use `LuauBufferView` only inside the current callback frame, and call `ToOwned()` before keeping it.
- Use [Strings](strings.md) when the data is text rather than arbitrary bytes.
