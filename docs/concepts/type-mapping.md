# Type mapping

Darp.Luau supports typed conversion between Luau values and managed types, but the exact conversion rules depend on which API surface you are using.

That distinction matters. A type that works when reading from a table does not automatically work in a `CreateFunction(...)` delegate signature, and a borrowed callback view does not behave like an owned reference.

## Core value families

| Luau value | Common managed forms | Notes |
| --- | --- | --- |
| `nil` | `LuauNil`, `null` in supported nullable cases | `nil` support depends on the API surface |
| `string` | `string`, `ReadOnlySpan<byte>`, `LuauString`, `LuauStringView` | spans and views can alias Luau memory |
| `number` | `double`, integral types, floating-point types, enums | a type only takes a number it can hold; see [Numeric conversions](#numeric-conversions) |
| `boolean` | `bool` | straightforward mapping |
| `table` | `LuauTable`, `LuauTableView` | owned vs borrowed distinction matters |
| `function` | `LuauFunction`, `LuauFunctionView` | owned vs borrowed distinction matters |
| `userdata` | `LuauUserdata`, `LuauUserdataView`, managed `ILuauUserData<T>` instances | managed userdata is library-defined userdata |
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

- `string` uses `null` to mean `nil`.
- `string.Empty` pushes an empty Luau string.
- `ReadOnlySpan<char>` currently treats an empty span as `nil`, so prefer `string` when empty string and `nil` need to stay distinct.
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

`CreateFunctionManual(...)` and userdata hooks expose callback arguments through `LuauArgs` or `LuauArgsSingle`.

These APIs mirror the same broad conversion families, but with callback-focused shapes:

- `TryReadNumber(...)`, `TryReadBoolean(...)`, `TryReadUtf8String(...)`, and `TryReadBuffer(...)`
- `TryRead*OrNil(...)` variants for supported nullable cases
- `TryReadLuauTable(...)`, `TryReadLuauFunction(...)`, `TryReadLuauString(...)`, `TryReadLuauBuffer(...)`, `TryReadLuauUserdata(...)` for borrowed views
- `TryReadUserdata<T>(...)` and `TryReadUserdataOrNil<T>(...)` for direct managed userdata resolution
- `TryReadLuauValue(...)` for dynamic inspection

```csharp
if (!args.TryReadNumber(1, out int amount, out string? error))
    return LuauReturn.Error(error);

if (!args.TryReadLuauTable(2, out LuauTableView table, out error))
    return LuauReturn.Error(error);
```

Borrowed `*View` values and any spans returned here are callback-scoped. Convert them to owned references with `ToOwned()` if they must outlive the current callback frame.

## Use `CreateFunction(...)` for supported delegate signatures

`CreateFunction(...)` uses a narrower set of conversions than the library as a whole.

It is a good fit for fixed signatures built from common primitives, supported nullable value types, enums, strings, span-based string or buffer parameters, `LuauValue`, managed userdata types generated with `[LuauUserdata]` or implemented manually with `ILuauUserData<TSelf>`, borrowed callback views, and top-level tuple returns whose elements are individually supported.

For userdata specifically, `CreateFunction(...)` supports two different shapes: `LuauUserdataView` for a borrowed raw userdata view, and self-typed managed userdata for generated `[LuauUserdata]` types or manual `ILuauUserData<TSelf>` implementations.

It is not the catch-all conversion surface for every wrapper type. Nested tuple returns and other unsupported delegate shapes still require `CreateFunctionManual(...)` and manual `LuauArgs` handling.

## Use `LuauValue` for dynamic code

`LuauValue` is the raw dynamic value wrapper used when you want to inspect or forward values without committing to a specific managed type up front.

You get it from APIs such as:

- `table[key]`,
- `TryGetLuauValue(...)`,
- `TryReadLuauValue(...)`.

Then reinterpret it with `TryGet<T>(...)`:

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

Important `LuauValue` rules:

- reference-backed values such as strings, tables, functions, userdata, and buffers can own registry references and should be disposed,
- converting a reference-backed `LuauValue` to an owned wrapper clones ownership, so the resulting wrapper must also be disposed,
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

- `TryReadNumber(...)` and `TryGetNumber(...)` return `false`. `TryReadNumber(...)` names the parameter, the type and the number in its error.
- A generated callback raises a Luau error, which the script can catch with `pcall(...)`.
- A typed result such as `Execute<int>()` throws `InvalidCastException`.

`TryReadNumber<T>(...)` and `TryGetNumber<T>(...)` are generic over the numeric type, so `out int`, `out byte`, `out Int128` or `out decimal` all use the same call.

An enum value does not have to be one of the named values: `7` is read into an enum that only names `1` and `2`. Check that in your callback when it matters.

## Failure modes

Conversions can fail when:

- the Luau runtime value has the wrong type,
- the target managed type is not supported on that particular API surface,
- a borrowed value is used after its callback frame ends,
- a reference-backed value is used with the wrong `LuauState`.

Use narrow, intentional conversions in your own host API instead of exposing every possible mapping at once.
