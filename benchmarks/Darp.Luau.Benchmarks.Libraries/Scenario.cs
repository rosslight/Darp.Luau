namespace Darp.Luau.Benchmarks.Libraries;

/// <summary> The libraries that are compared. Each benchmark class carries one of these names as its category. </summary>
internal static class Library
{
    public const string DarpLuau = "Darp.Luau";
    public const string NuLua = "NuLua";
    public const string NLua = "NLua";
    public const string LuaCSharp = "Lua-CSharp";

    /// <summary> All libraries, in the order they are reported. </summary>
    public static readonly string[] All = [DarpLuau, NuLua, NLua, LuaCSharp];
}

/// <summary>
/// What every library is asked to do. Each library has one benchmark class with one method per scenario,
/// described by the constants below, so that results can be matched across libraries.
/// </summary>
internal static class Scenario
{
    public const string CreateState = "Create and dispose a state";
    public const string CallLuaFunction = "Call a Lua function from C#";
    public const string CallManagedFunction = "Call a C# function from Lua";
    public const string TableSetAndGet = "Set and get a table field";
    public const string RunScript = "Run fib(20) in Lua";

    /// <summary> All scenarios, in the order they are reported. </summary>
    public static readonly string[] All =
    [
        CallLuaFunction,
        CallManagedFunction,
        TableSetAndGet,
        RunScript,
        CreateState,
    ];

    /// <summary> Number of calls into managed code that <see cref="Script"/>'s <c>call_managed_add</c> makes. </summary>
    public const int ManagedCallsPerInvoke = 1000;

    // Every number crosses the boundary as a floating-point value. Lua 5.4 has an integer subtype and would
    // otherwise do integer arithmetic where the other runtimes use floating point.

    /// <summary> Arguments of <c>add</c> in <see cref="CallLuaFunction"/>. </summary>
    public const double AddLeft = 1.0;

    /// <inheritdoc cref="AddLeft"/>
    public const double AddRight = 2.0;

    /// <summary> Argument of <c>call_managed_add</c> in <see cref="CallManagedFunction"/>. </summary>
    public const double ManagedCalls = ManagedCallsPerInvoke;

    /// <summary> Value written and read back in <see cref="TableSetAndGet"/>. </summary>
    public const double TableValue = 42.0;

    /// <summary> Argument of <c>fib</c> in <see cref="RunScript"/>. </summary>
    public const double FibonacciInput = 20.0;

    /// <summary> The script shared by all libraries. It stays within what Lua 5.2, Lua 5.4 and Luau have in common. </summary>
    public const string Script = """
        function add(a, b)
            return a + b
        end

        function call_managed_add(n)
            local sum = 0
            -- Starting at 1.0 makes Lua 5.4 count in floating point, like the other runtimes.
            for i = 1.0, n do
                sum = sum + managed_add(i, i)
            end
            return sum
        end

        function fib(n)
            if n < 2 then
                return n
            end
            return fib(n - 1) + fib(n - 2)
        end
        """;
}
