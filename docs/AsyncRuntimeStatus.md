# Async runtime draft: intent and current status

## Why this exists

A host may need to await managed work while a Luau script is running. The script should be
able to call that host function normally; the .NET caller awaits the enclosing execution.
The June 24, 2026 design discussion retained the fast synchronous path and chose a separate
`ValueTask<LuauReturn>` callback builder, coroutine execution, and a simple per-state queue.
The follow-up implementation request covered **phase 2** of [the plan](Brainstorming.md).

This draft preserves that work and its regression tests. It does not complete the later
function-invocation or source-generator phases.

## What is implemented

- `LuauChunk.ExecuteAsync` supports no result, typed results, tuples, and `ExecuteMultiAsync`.
- `CreateAsyncFunctionBuilder` registers manual callbacks returning `ValueTask<LuauReturn>`.
- Pending managed callbacks yield a Luau coroutine; a per-state queue serializes its resumes.
- Async chunk source, arguments, and environment references are retained for deferred work.
- Callback errors are translated to Lua errors so `pcall` can catch them. Unhandled errors
  become `LuaException`; external cancellation cancels the outer operation.
- State disposal faults pending operations. A pre-canceled token does not retain its frame.
- Stack-backed `LuauArgs` becomes invalid when an async callback yields. Read arguments into
  managed values before awaiting; the original plan's lifetime-through-completion is a goal
  that was superseded in this draft after the June lifetime review.

For example, the existing yield/resume test registers `add_later`, reads its numeric argument
before awaiting a managed gate, and returns the sum. Luau executes `return add_later(40)`;
completing the gate with `2` lets the .NET `ExecuteAsync<int>` caller receive `42`.

## Known gaps before readiness

1. **Scheduling and thread ownership remain unresolved.** The queue serializes async items,
   but it does not serialize ordinary public state calls against them. The current value-task
   source drives resumption from completion/status observation and does not reliably return
   to the original managed thread. `CSharpFunction_AsyncBuilder_ShouldNotResumeLuauOnCompletionThread`
   fails its stronger assertion that the resume thread equals the invoking thread. Define
   host dispatch/ownership before claiming this contract; do not hide the failure by dropping
   the assertion. Review multiple successive suspensions and completion-continuation handling
   together with the scheduler, since a managed callback completion is not necessarily completion
   of the enclosing Lua operation.
2. **Async errors depend on the wrapper's visible global `error`.** Review behavior with
   `LuauLibraries.None` and with replaced base globals. The ordinary result-push failure is
   contained by the marshal helper, but the full unmanaged continuation boundary still needs
   review; the direct helper test does not prove every path is contained.
3. **Cancellation and late completion need more lifetime coverage.** External cancellation
   wins even if callback code catches cancellation and returns a normal Lua error, unlike the
   original plan's caveat. Review owned callback results arriving after cancellation/disposal
   and cleanup if typed result conversion throws.
4. **Chunk loading has a separate async implementation.** Review parity for UTF-8 input,
   custom environments, supplied arguments, compile errors, and reference cleanup. Current
   tests do not establish all these branches or every zero-to-four-value callback result shape.
5. **Later phases are still planned.** `LuauFunction.InvokeAsync`, generated async module
   exports, token injection, and async userdata dispatch do not exist yet. Module loading,
   `require`, and userdata properties retain synchronous behavior. The draft's error text
   mentions `InvokeAsync`, which is not yet an available alternative.

The current `LuauState` remains not thread-safe. This draft does not establish safe parallel
use of normal state APIs alongside an async execution.

## Validation recorded on October 1, 2026

Windows x64, .NET SDK 10.0.302, Debug configuration:

| Suite | Result | What it establishes |
| --- | --- | --- |
| Runtime | 476 passed, 1 failed, 477 total | Includes sync/async execution, yield/resume, independent pending operations, error propagation, argument lifetime, cancellation, disposal, and the advertisement-analyzer smoke test. Thread affinity fails. |
| Generator | 55 passed, 0 failed | Existing generator diagnostics and generated-source snapshots still pass with the local dependency updates. This is not coverage of generated async exports. |

The runtime build reports two CA1815 warnings for `LuauReturn` equality. The CI warning-as-error
build therefore remains a readiness concern. NativeAOT publishing, Release, and other platforms
were not run locally. A new independent review was unavailable in this side conversation;
June review findings were recovered and checked against the present draft during self-review.

Test commands and TRX results are retained locally under `artifacts/async-pr-results` and
separate `artifacts/async-pr-*-test-out` directories. Build artifacts are not committed.
