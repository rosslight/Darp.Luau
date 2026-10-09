namespace Darp.Luau;

/// <summary> The metamethods a userdata type can declare. </summary>
/// <remarks>
/// A metamethod receives its operands as Luau passes them. For a binary operator the instance can therefore be
/// either operand: Luau uses the metamethod of the left operand, and that of the right one only when the left has
/// none.
/// </remarks>
public enum LuauMetamethod
{
    /// <summary> <c>__index</c>: reads a key that is not a declared member. Receives the instance and the key. </summary>
    Index,

    /// <summary>
    /// <c>__newindex</c>: writes a key that is not a declared member. Receives the instance, the key and the value.
    /// </summary>
    NewIndex,

    /// <summary> <c>__call</c>: calls the instance. Receives the instance and the arguments. Can await. </summary>
    Call,

    /// <summary> <c>__tostring</c>: the text <c>tostring</c> returns. Receives the instance. </summary>
    ToString,

    /// <summary> <c>__eq</c>: <c>a == b</c> for two different instances of the same type. </summary>
    Eq,

    /// <summary> <c>__lt</c>: <c>a &lt; b</c> for two instances of the same type. </summary>
    Lt,

    /// <summary> <c>__le</c>: <c>a &lt;= b</c> for two instances of the same type. </summary>
    Le,

    /// <summary> <c>__len</c>: <c>#a</c>. Must return a number. </summary>
    Len,

    /// <summary> <c>__concat</c>: <c>a .. b</c>. </summary>
    Concat,

    /// <summary> <c>__unm</c>: <c>-a</c>. </summary>
    Unm,

    /// <summary> <c>__add</c>: <c>a + b</c>. </summary>
    Add,

    /// <summary> <c>__sub</c>: <c>a - b</c>. </summary>
    Sub,

    /// <summary> <c>__mul</c>: <c>a * b</c>. </summary>
    Mul,

    /// <summary> <c>__div</c>: <c>a / b</c>. </summary>
    Div,

    /// <summary> <c>__mod</c>: <c>a % b</c>. </summary>
    Mod,

    /// <summary> <c>__pow</c>: <c>a ^ b</c>. </summary>
    Pow,

    /// <summary> <c>__idiv</c>: <c>a // b</c>. </summary>
    IDiv,
}
