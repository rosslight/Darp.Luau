# Type mapping

Darp.Luau supports typed conversion between Luau values and managed types, but the exact conversion rules depend on which API surface you are using.

That distinction matters. A type that works when reading from a table does not automatically work in a `CreateFunction(...)` delegate signature, and a borrowed callback view does not behave like an owned reference.

## Core value families

| Luau value | Common managed forms | Notes |
| --- | --- | --- |
| `nil` | `null` of a nullable value type such as `int?`, or of a reference type where the API reads optional values | `nil` support depends on the API surface |
| `string` | `string`, `ReadOnlySpan<byte>`, `LuauString`, `LuauStringView` | spans and views can alias Luau memory |
| `number` | `double`, integral types, floating-point types, `decimal`, enums | a type only takes a number it can hold; see [Numeric conversions](#numeric-conversions) |
| `boolean` | `bool` | straightforward mapping |
| `table` | `LuauTable`, `LuauTableView` | owned vs borrowed distinction matters |
| `function` | `LuauFunction`, `LuauFunctionView` | owned vs borrowed distinction matters |
| `userdata` | `LuauUserdata`, `LuauUserdataView`, managed `ILuauUserdata<T>` instances | managed userdata is library-defined userdata |
| `buffer` | `byte[]`, `ReadOnlySpan<byte>`, `LuauBuffer`, `LuauBufferView` | spans and views can alias Luau memory |

Vector and thread values are not currently documented as managed interop surfaces.

For concrete string and buffer API matrices and examples, see [Strings](../features/strings.md) and [Buffers](../features/buffers.md).

## Push values into Luau with `IntoLuau`

`IntoLuau` is the temporary carrier used by APIs that push managed values into Luau.

You usually rely on implicit conversions at the call site:

```csharp
table.Set("name", "Ada");
table.Set("enabled", true);
table.Set("bytes", new byte[] { 1, 2, 3 });

double result = add.Invoke<double>(1, 2);

return LuauReturn.Ok("ok", 42);
```

You see it most often when:

- setting globals or table fields,
- passing arguments to `LuauFunction.Invoke(...)`,
- returning values from `LuauReturn.Ok(...)` or `LuauReturnSingle.Ok(...)`.

`IntoLuau` is a `ref struct` and intentionally temporary. Treat it as a call-site conversion type, not something to cache.

### Common write-side rules

- Only `null` means `nil`: a `null` `string`, `byte[]` or nullable value.
- An empty `string`, `ReadOnlySpan<char>` or `byte[]` pushes an empty Luau string or buffer.
- Passing `byte[]` copies managed data into a Luau buffer.
- Passing owned wrappers such as `LuauTable`, `LuauFunction`, `LuauString`, `LuauBuffer`, or `LuauUserdata` reuses the existing Luau-backed value without creating a second owned wrapper.
- A reference-backed `LuauValue` does the same when you pass it back into Luau.
- Passing borrowed `*View` values keeps the same callback-frame lifetime constraints.
- Reference-backed values are bound to one `LuauState`; cross-state usage is invalid.

`ReadOnlySpan<char>` is mainly a write-side and callback-signature shape. Normal table and global string reads use `string`, `ReadOnlySpan<byte>`, `LuauString`, or `LuauStringView` instead.

### Custom write-side conversions

Your own types can participate by defining `implicit operator IntoLuau`.

For primitive-style wrappers, forward to an existing supported value:

```csharp
public readonly record struct UserId(int Value)
{
    public static implicit operator IntoLuau(UserId value) => value.Value;
}
```

For managed userdata, forward to `IntoLuau.FromUserdata(...)`.

## Read values from tables and globals

Tables and globals use a family of typed read methods:

- `Get*` for required values,
- `TryGet*` for optional or external data,
- `*OrNil` when `nil` is a valid result,
- `GetLuau*` and `TryGetLuau*` when you want an owned Luau wrapper instead of an immediate managed copy.

Examples:

```csharp
string name = lua.Globals.GetUtf8String("name");
bool hasScore = lua.Globals.TryGetNumber("score", out int score);
byte[]? maybeBuffer = lua.Globals.GetBufferOrNil("payload");
using LuauTable nested = lua.Globals.GetLuauTable("config");
```

Important distinctions:

- `GetUtf8String(...)` and `GetBuffer(...)` return managed copies.
- `LuauMarshal.TryGetUtf8StringSpan(...)` and `LuauMarshal.TryGetBufferSpan(...)` expose Luau-owned memory of a table value without a copy. Nothing keeps that memory alive, so consume it immediately.
- `GetLuauTable(...)`, `GetLuauFunction(...)`, `GetLuauString(...)`, `GetLuauBuffer(...)`, and `GetLuauUserdata(...)` return owned references that need disposal.
- `TryGetUserdata<T>(...)` resolves directly back to your managed userdata instance when the value is managed userdata created by this library and matches `T`.

## Read callback arguments with `LuauArgs`

`CreateFunctionManual(...)` and userdata callbacks expose callback arguments through `LuauArgs`. `args[1]` is the first argument, and a userdata setter receives the assigned value the same way. Both are a `LuauValueView`, which reads with the methods of `LuauValue` below: `Get<T>()`, `GetOrNull<T>()`, `TryGet<T>(...)` and `TryGetOrNull<T>(...)`.

```csharp
int amount = args[1].Get<int>();
string? label = args[2].GetOrNull<string>();
LuauTableView table = args[3].Get<LuauTableView>();
```

An argument reads as every type a `LuauValue` reads as, and as two kinds of borrowed value on top:

- a borrowed view: `LuauTableView`, `LuauFunctionView`, `LuauStringView`, `LuauBufferView`, `LuauUserdataView`, `LuauCoroutineView`,
- `ReadOnlySpan<byte>` for the UTF-8 bytes of a string.

An argument the script did not pass is `nil`. A read that fails throws `LuaGetException`, which reaches the script as a Luau error such as `invalid argument #1 (number expected, got string)`; a callback does not catch it. See [Functions](../features/functions.md#use-createfunctionmanual-for-manual-callbacks).

Borrowed `*View` values and any spans returned here are callback-scoped. Convert them to owned references with `ToOwned()` if they must outlive the current callback frame.

## Use `CreateFunction(...)` for supported delegate signatures

`CreateFunction(...)` uses a narrower set of conversions than the library as a whole.

It is a good fit for fixed signatures built from common primitives, supported nullable value types, enums, strings, span-based string or buffer parameters, `LuauValue`, managed userdata types generated with `[LuauUserdata]` or implemented manually with `ILuauUserdata<TSelf>`, borrowed callback views, and top-level tuple returns whose elements are individually supported.

For userdata specifically, `CreateFunction(...)` supports two different shapes: `LuauUserdataView` for a borrowed raw userdata view, and self-typed managed userdata for generated `[LuauUserdata]` types or manual `ILuauUserdata<TSelf>` implementations.

It is not the catch-all conversion surface for every wrapper type. Nested tuple returns and other unsupported delegate shapes still require `CreateFunctionManual(...)` and reading `LuauArgs` yourself.

## Use `LuauValue` for dynamic code

`LuauValue` is the raw dynamic value wrapper used when you want to inspect or forward values without committing to a specific managed type up front.

You get it from APIs such as:

- `table[key]`,
- `TryGetLuauValue(...)`,
- `args[1].Get<LuauValue>()`.

Then read it as a managed type:

```csharp
if (lua.Globals.TryGetLuauValue("payload", out LuauValue value))
{
    using (value)
    {
        if (value.TryGet(out string? text))
        {
            // use text
        }
    }
}
```

| Method | `nil` | Another type |
| --- | --- | --- |
| `Get<T>()` | throws `LuaGetException` | throws `LuaGetException` |
| `GetOrNull<T>()` | `null` | throws `LuaGetException` |
| `TryGet<T>(out T value)` | `false` | `false` |
| `TryGetOrNull<T>(out T? value)` | `true` with `null` | `false` |

`T` is any type a Luau value has a managed form for: a number type, an enum, `bool`, `string`, `byte[]`, a managed userdata type, an owned wrapper such as `LuauTable`, or `LuauValue` for a copy. The same types work as the result of `Invoke<T>(...)`, `Execute<T>()` and `Resume<T>(...)`.

The `nil` column holds for a `T` that has no value for `nil`. Two kinds of `T` have one and read it with `Get<T>()` and `TryGet<T>(...)`:

- a nullable value type: `Get<int?>()` and `TryGet(out int? number)` read `nil` as `null`,
- `LuauValue` itself, which reads `nil` as a nil value and therefore has no nullable form.

A reference type cannot say that `nil` is fine, because `string` and `string?` are the same type at run time, so it has the `OrNull` methods.

A typed result has no `OrNull` form: `Invoke<string>(...)` returns `null` when the function returns `nil`.

Important `LuauValue` rules:

- reference-backed values such as strings, tables, functions, userdata, and buffers can own registry references and should be disposed,
- reading a reference-backed `LuauValue` as an owned wrapper gives you a reference of its own, so the resulting wrapper must also be disposed,
- reading a `LuauValue` that was disposed throws `ObjectDisposedException`,
- `LuauValueType.Nil` is the default value and represents `nil`.

## Numeric conversions

A Luau number is a `double`. Reading it as another numeric type only succeeds when that type can hold the number, so a script cannot hand your code a different number than the one it wrote.

| Target type | Takes | Rejects |
| --- | --- | --- |
| integer types (`int`, `byte`, `long`, `UInt128`, ...) | whole numbers in the range of the type | fractions such as `1.5`, `-1` for an unsigned type, numbers out of range, NaN, infinity |
| `float`, `Half` | every number, as the nearest value the type has | nothing; a number out of range becomes infinity |
| `decimal` | every finite number in its range, rounded to its precision | NaN, infinity, numbers out of range |
| `double` | every number | nothing |
| enums | whole numbers in the range of the underlying type | the same as that integer type |

What a rejected number does depends on where it is read:

- `TryGetNumber(...)` and `TryGet(...)` return `false`.
- An argument read with `Get<T>()`, in a manual or a generated callback, raises a Luau error that names the argument, the type and the number. The script can catch it with `pcall(...)`.
- A typed result such as `Execute<int>()` throws `LuaGetException`.

`TryGetNumber<T>(...)` is generic over the numeric type, so `out int`, `out byte`, `out Int128` or `out decimal` all use the same call.

An enum value does not have to be one of the named values: `7` is read into an enum that only names `1` and `2`. Check that in your callback when it matters.

## Failure modes

Conversions can fail when:

- the Luau runtime value has the wrong type,
- the target managed type is not supported on that particular API surface,
- a borrowed value is used after its callback frame ends,
- a reference-backed value is used with the wrong `LuauState`.

Use narrow, intentional conversions in your own host API instead of exposing every possible mapping at once.
