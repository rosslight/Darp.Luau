# Advertisement-analyzer embedding experiment

This is a separate synchronous integration experiment, preserved alongside the async draft.
It explores a managed host module exposing protocol/field userdata and retaining a Lua
callback that the host can invoke later. It does not define a finished advertisement-analysis
API and does not exercise async callbacks.

## Executable smoke test

`tests/Darp.Luau.Tests/AdvAnalyzerTests.cs` registers the `aa` module, creates a protocol and a
field, and lets Lua install a dissector through `p:set_dissector(...)`. The host invokes the
owned callback with event/tree userdata and checks that it adds the expected tree entry.
This verifies that module generation, userdata generation, and borrowed-to-owned callback
conversion can be used together. It passed in the October 1, 2026 runtime-suite run.

## Design sketches

`tests/Darp.Luau.Tests/adv.lua` and `_c_api.lua` are exploratory API sketches. They are not
loaded by the test suite. The `...` placeholders, table-argument factories, direct dissector
assignment, and `register_dissector` naming in the sketch are not supported by the current
smoke test, which uses positional factories, `set_dissector`, and `register_protocol`.

The C# field types and unimplemented methods are also prototype scaffolding. A later change
should settle the public script contract, complete only the needed operations, exercise the
actual script text, and define ownership/cleanup for registered protocols and callbacks.
Keep this experiment separate from the async-runtime acceptance criteria.
