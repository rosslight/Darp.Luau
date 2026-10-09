using Darp.Luau.Utils;

namespace Darp.Luau;

/// <summary> Receives what Luau can do with the managed type <typeparamref name="T"/>. </summary>
/// <typeparam name="T">Managed userdata type.</typeparam>
/// <remarks>
/// An instance has getters, setters and methods. Reading a method gives a function, so <c>value:name()</c> and
/// <c>value.name(value)</c> are the same call. A declared name is never passed on to
/// <see cref="LuauMetamethod.Index"/> or <see cref="LuauMetamethod.NewIndex"/>: writing a name that only has a
/// getter, reading one that only has a setter, and assigning a method are errors.
/// </remarks>
public sealed class LuauUserdataRegistry<T>
    where T : class
{
    [Flags]
    private enum Accessors
    {
        None = 0,
        Getter = 1,
        Setter = 2,
        Method = 4,
    }

    private readonly List<UserdataMember> _members = [];
    private readonly List<UserdataMemberName> _methods = [];
    private readonly List<UserdataMemberName> _getters = [];
    private readonly List<UserdataMemberName> _setters = [];
    private readonly List<UserdataMemberName> _metamethods = [];
    private readonly Dictionary<string, Accessors> _instanceNames = new(StringComparer.Ordinal);
    private readonly HashSet<LuauMetamethod> _declaredMetamethods = [];
    private int _indexMember = UserdataDescription.NoMember;
    private int _newIndexMember = UserdataDescription.NoMember;
    private string? _typeName;
    private bool _isBuilt;

    internal LuauUserdataRegistry() { }

    /// <summary> Gets or sets the name scripts see: the result of <c>typeof(value)</c>, also used in Luau's errors. </summary>
    /// <remarks> Without a name, <c>typeof(value)</c> is <c>userdata</c>. </remarks>
    /// <exception cref="ArgumentException">Thrown when the name is empty or the name of a built-in Luau type.</exception>
    public string? TypeName
    {
        get => _typeName;
        set
        {
            ThrowIfBuilt();
            if (value is not null)
            {
                ThrowIfNotAName(value, nameof(value));
                if (
                    value
                    is "nil"
                        or "boolean"
                        or "number"
                        or "string"
                        or "table"
                        or "function"
                        or "thread"
                        or "userdata"
                        or "vector"
                        or "buffer"
                )
                {
                    throw new ArgumentException($"'{value}' is the name of a built-in Luau type.", nameof(value));
                }
            }
            _typeName = value;
        }
    }

    /// <summary> Adds a member that scripts read as <c>value.name</c>. </summary>
    /// <param name="name">Name of the member.</param>
    /// <param name="getter">Returns the value of the member.</param>
    /// <exception cref="ArgumentException">Thrown when the name already has a getter or is a method.</exception>
    public void AddGetter(string name, LuauGetter<T> getter)
    {
        ArgumentNullException.ThrowIfNull(getter);
        ClaimInstanceName(name, Accessors.Getter, conflicts: Accessors.Getter | Accessors.Method);
        _getters.Add(AddMember(name, new UserdataGetter<T>(name, getter)));
    }

    /// <summary> Adds a member that scripts write as <c>value.name = x</c>. </summary>
    /// <param name="name">Name of the member.</param>
    /// <param name="setter">Stores the assigned value.</param>
    /// <exception cref="ArgumentException">Thrown when the name already has a setter or is a method.</exception>
    public void AddSetter(string name, LuauSetter<T> setter)
    {
        ArgumentNullException.ThrowIfNull(setter);
        ClaimInstanceName(name, Accessors.Setter, conflicts: Accessors.Setter | Accessors.Method);
        _setters.Add(AddMember(name, new UserdataSetter<T>(name, setter)));
    }

    /// <summary> Adds a method that scripts call as <c>value:name(...)</c>. </summary>
    /// <param name="name">Name of the method.</param>
    /// <param name="method">Runs the method. Its arguments do not include the instance.</param>
    /// <exception cref="ArgumentException">Thrown when the name is already declared.</exception>
    public void AddMethod(string name, LuauMethod<T> method)
    {
        ArgumentNullException.ThrowIfNull(method);
        ClaimInstanceName(name, Accessors.Method, conflicts: Accessors.Getter | Accessors.Setter | Accessors.Method);
        _methods.Add(AddMember(name, new UserdataMethod<T>(name, method)));
    }

    /// <summary> Adds a metamethod. </summary>
    /// <param name="metamethod">The metamethod.</param>
    /// <param name="callback">Runs it with the operands as Luau passes them.</param>
    /// <remarks>
    /// Only <see cref="LuauMetamethod.Call"/> can await. <see cref="LuauMetamethod.Index"/> and
    /// <see cref="LuauMetamethod.NewIndex"/> are only reached for keys that are not declared members.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when the metamethod is already declared.</exception>
    public void AddMetamethod(LuauMetamethod metamethod, LuauCallback callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ThrowIfBuilt();
        string name = GetMetamethodName(metamethod);
        if (!_declaredMetamethods.Add(metamethod))
            throw new ArgumentException($"Userdata type '{typeof(T)}' already declares '{name}'.", nameof(metamethod));

        UserdataMemberName member = AddMember(name, new UserdataCallback(name, callback));
        switch (metamethod)
        {
            case LuauMetamethod.Index:
                _indexMember = member.Member;
                break;
            case LuauMetamethod.NewIndex:
                _newIndexMember = member.Member;
                break;
            default:
                _metamethods.Add(member);
                break;
        }
    }

    internal UserdataDescription Build()
    {
        _isBuilt = true;
        return new UserdataDescription
        {
            TypeName = _typeName is null ? null : UserdataDescription.ToLuauName(_typeName),
            Members = [.. _members],
            Methods = [.. _methods],
            Getters = [.. _getters],
            Setters = [.. _setters],
            Metamethods = [.. _metamethods],
            IndexMember = _indexMember,
            NewIndexMember = _newIndexMember,
        };
    }

    private UserdataMemberName AddMember(string name, UserdataMember member)
    {
        _members.Add(member);
        return new UserdataMemberName(UserdataDescription.ToLuauName(name), _members.Count - 1);
    }

    private void ClaimInstanceName(string name, Accessors accessor, Accessors conflicts)
    {
        ThrowIfBuilt();
        ThrowIfNotAName(name, nameof(name));
        Accessors declared = _instanceNames.GetValueOrDefault(name);
        if ((declared & conflicts) != Accessors.None)
            throw new ArgumentException($"Userdata type '{typeof(T)}' already declares '{name}'.", nameof(name));
        _instanceNames[name] = declared | accessor;
    }

    private void ThrowIfBuilt()
    {
        if (_isBuilt)
            throw new InvalidOperationException("A userdata registry can only be used while its Register runs.");
    }

    private static void ThrowIfNotAName(string name, string parameterName)
    {
        ArgumentException.ThrowIfNullOrEmpty(name, parameterName);
        // Luau reads the name up to its first zero.
        if (name.Contains('\0', StringComparison.Ordinal))
            throw new ArgumentException("A name cannot contain a zero character.", parameterName);
    }

    private static string GetMetamethodName(LuauMetamethod metamethod) =>
        metamethod switch
        {
            LuauMetamethod.Index => "__index",
            LuauMetamethod.NewIndex => "__newindex",
            LuauMetamethod.Call => "__call",
            LuauMetamethod.ToString => "__tostring",
            LuauMetamethod.Eq => "__eq",
            LuauMetamethod.Lt => "__lt",
            LuauMetamethod.Le => "__le",
            LuauMetamethod.Len => "__len",
            LuauMetamethod.Concat => "__concat",
            LuauMetamethod.Unm => "__unm",
            LuauMetamethod.Add => "__add",
            LuauMetamethod.Sub => "__sub",
            LuauMetamethod.Mul => "__mul",
            LuauMetamethod.Div => "__div",
            LuauMetamethod.Mod => "__mod",
            LuauMetamethod.Pow => "__pow",
            LuauMetamethod.IDiv => "__idiv",
            _ => throw new ArgumentOutOfRangeException(nameof(metamethod), metamethod, "Unknown metamethod."),
        };
}
