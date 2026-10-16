using Darp.Luau.Utils;

namespace Darp.Luau;

/// <summary>
/// Releases the managed objects of the userdata created while it is open, see
/// <see cref="LuauState.BeginUserdataScope"/>. Dispose it once, after the scopes that began inside it.
/// </summary>
/// <remarks> A ref struct, so that a scope ends on the thread and in the call it began in. </remarks>
public readonly ref struct UserdataScope : IDisposable
{
    private readonly UserdataObjects? _objects;
    private readonly long _id;

    internal UserdataScope(UserdataObjects objects, long id)
    {
        _objects = objects;
        _id = id;
    }

    /// <summary> Releases the objects. A script that uses one of their userdata afterwards gets an error. </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the scope was disposed already, or a scope that began inside it is still open.
    /// </exception>
    public void Dispose() => _objects?.EndScope(_id);
}
