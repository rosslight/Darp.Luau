namespace Darp.Luau;

/// <summary> The status of a <see cref="LuauCoroutine"/>, as reported by <c>coroutine.status</c>. </summary>
public enum LuauCoroutineStatus
{
    /// <summary> The coroutine has not started yet or yielded. Only a suspended coroutine can be resumed. </summary>
    Suspended,

    /// <summary> A host call or another coroutine is executing the coroutine. </summary>
    Running,

    /// <summary> The coroutine returned from its function. </summary>
    Finished,

    /// <summary> The coroutine stopped with an error. </summary>
    Error,
}
