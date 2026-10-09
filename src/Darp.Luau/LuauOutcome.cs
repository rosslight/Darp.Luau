using System.Diagnostics.CodeAnalysis;

namespace Darp.Luau;

/// <summary>
/// The outcome of a callback that returns no value, such as a <see cref="LuauSetter{T}"/>.
/// Use <see cref="Ok"/> when the assignment was handled, or <see cref="Error(string)"/> to report an error.
/// </summary>
/// <remarks>
/// The default value represents an error with message <c>Unknown error</c>.
/// </remarks>
public readonly ref struct LuauOutcome
{
    private readonly string? _error;

    /// <summary> Gets whether this callback result is successful. </summary>
    public bool IsOk { get; }

    private LuauOutcome(bool isOk, string? error = null)
    {
        IsOk = isOk;
        _error = error;
    }

    /// <summary> Creates a successful callback result that indicates the assignment was handled. </summary>
    public static LuauOutcome Ok() => new(isOk: true);

    /// <summary> Creates a failed callback result with an error message. </summary>
    /// <param name="error">Error message reported to the caller.</param>
    /// <remarks>When the provided text is empty or whitespace, <c>Unknown error</c> is used.</remarks>
    public static LuauOutcome Error(string error) => new(isOk: false, error: error);

    /// <summary> Gets the error message when this result is not successful. </summary>
    /// <param name="error">Receives the error message when <see cref="IsOk"/> is <c>false</c>.</param>
    /// <returns><c>true</c> when an error is present; otherwise <c>false</c>.</returns>
    internal bool TryGetError([NotNullWhen(true)] out string? error)
    {
        if (IsOk)
        {
            error = null;
            return false;
        }

        error = _error ?? "Unknown error";
        return true;
    }
}
