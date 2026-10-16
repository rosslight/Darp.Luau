namespace Darp.Luau.Tests;

internal static class AwaitTestExtensions
{
    /// <summary>
    /// Awaits the work <paramref name="start"/> returns. When the callback cannot suspend its coroutine, the work is
    /// not started and the callback fails with the reason.
    /// </summary>
    public static LuauReturn AwaitOrError(this LuauArgs args, Func<ValueTask<LuauReturn>> start) =>
        args.CreateAwaiter().Await(start());
}
