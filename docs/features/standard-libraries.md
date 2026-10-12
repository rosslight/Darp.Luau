# Standard libraries

Standard libraries are built-in Luau globals such as `math`, `table`, and `string`. They are state configuration, not modules loaded through `require(...)`.

Choose built-in Luau libraries when you create the state, or load additional libraries later:

```csharp
using var lua = new LuauState(LuauLibraries.Math | LuauLibraries.String);

lua.LoadStandardLibraries(LuauLibraries.Buffer);
```

Available flags include:

- `Base`
- `Coroutine`
- `Table`
- `Os`
- `String`
- `Math`
- `Debug`
- `Utf8`
- `Bit32`
- `Buffer`
- `Vector`

Important details:

- `LuauLibraries.None` loads no standard libraries.
- `EnabledLibraries` shows the effective built-in library set for the state.
- `LoadStandardLibraries(...)` is idempotent; already loaded libraries are ignored.
- `LuauLibraries.Buffer` enables Luau's script-side `buffer` library. Host-side buffer interop such as `CreateBuffer(...)`, `Get<byte[]>(...)`, or passing `byte[]` does not depend on that flag.

Use [Modules and require](modules.md) when Luau code should load a host-provided or file-backed module with `require(...)`.
