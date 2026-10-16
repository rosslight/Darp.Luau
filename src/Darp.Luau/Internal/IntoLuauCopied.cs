using System.Diagnostics;
using System.Text;
using Darp.Luau.Native;
using Darp.Luau.Utils;
using static Darp.Luau.Native.LuauNative;

namespace Darp.Luau.Internal;

internal readonly struct IntoLuauCopied
{
    private enum Kind
    {
        Nil = 0,
        String,
        Buffer,
        Value,
        Userdata,
        BorrowedValue,
        BorrowedReference,
    }

    // The kinds share their storage: every LuauReturn embeds four copies, and the work of an awaiting managed
    // callback holds its LuauReturn on the heap.
    private readonly Kind _type;

    /// <summary> The value of <see cref="Kind.Value"/> and <see cref="Kind.BorrowedValue"/>. </summary>
    private readonly LuauValue _value;

    /// <summary> The string, buffer, userdata or borrowed reference of the other kinds. </summary>
    private readonly object? _object;

    private IntoLuauCopied(Kind type, LuauValue value) => (_type, _value) = (type, value);

    private IntoLuauCopied(Kind type, object value) => (_type, _object) = (type, value);

    internal static IntoLuauCopied FromBool(bool value) => new(Kind.Value, (LuauValue)value);

    // Luau has a single number type: an integer is pushed as the same number.
    internal static IntoLuauCopied FromNumber(double value) => new(Kind.Value, (LuauValue)value);

    internal static IntoLuauCopied FromString(string value) => new(Kind.String, value);

    internal static IntoLuauCopied FromBuffer(byte[] value) => new(Kind.Buffer, value);

    internal static IntoLuauCopied FromValue(LuauValue value) => new(Kind.Value, value);

    /// <summary> Refers to a value the caller keeps owning; it must stay alive until the copy is pushed. </summary>
    internal static IntoLuauCopied FromBorrowedValue(LuauValue value) => new(Kind.BorrowedValue, value);

    /// <summary> Refers to a reference the caller keeps owning; it must stay alive until the copy is pushed. </summary>
    internal static IntoLuauCopied FromBorrowedReference(RegistryReferenceTracker.TrackedReference reference) =>
        new(Kind.BorrowedReference, reference);

    internal static IntoLuauCopied FromUserdata(object userdata, Action<LuauState, object> push) =>
        new(Kind.Userdata, new CopiedUserdata(userdata, push));

    /// <summary> A userdata to push later, with the function that pushes an object of its type. </summary>
    private sealed class CopiedUserdata(object userdata, Action<LuauState, object> push)
    {
        private readonly object _userdata = userdata;
        private readonly Action<LuauState, object> _push = push;

        public void Push(LuauState state) => _push(state, _userdata);
    }

    internal unsafe void Push(LuauState state, lua_State* L)
    {
        if (!state.OwnsThread(L))
            throw new InvalidOperationException("Cross-state value push is not allowed.");

        switch (_type)
        {
            case Kind.String:
                Debug.Assert(_object is not null);
                var text = (string)_object;
                if (text.Length > 256)
                {
                    Span<byte> utf8 = new byte[Encoding.UTF8.GetByteCount(text)];
                    int length = Encoding.UTF8.GetBytes(text, utf8);
                    LuauStateMarshal.PushString(L, utf8[..length]);
                }
                else
                {
                    Span<byte> utf8 = stackalloc byte[Encoding.UTF8.GetByteCount(text)];
                    int length = Encoding.UTF8.GetBytes(text, utf8);
                    LuauStateMarshal.PushString(L, utf8[..length]);
                }
                break;
            case Kind.Buffer:
                Debug.Assert(_object is not null);
                var buffer = (byte[])_object;
                void* pDest = lua_newbuffer(L, (nuint)buffer.Length);
                var destination = new Span<byte>(pDest, buffer.Length);
                buffer.CopyTo(destination);
                break;
            case Kind.Value:
            case Kind.BorrowedValue:
                _value.Push(L);
                break;
            case Kind.BorrowedReference:
                Debug.Assert(_object is not null);
                var reference = (RegistryReferenceTracker.TrackedReference)_object;
                if (!ReferenceEquals(state, reference.State))
                    throw new InvalidOperationException("Cross-state reference usage is not allowed.");
                if (!reference.IsTracked)
                    throw new ObjectDisposedException(
                        nameof(LuauValue),
                        "The argument was disposed before it was used."
                    );
#pragma warning disable CA2000 // The pushed value is intentionally transferred to the caller's stack protocol and must remain on the stack.
                _ = reference.PushToTop();
#pragma warning restore CA2000
                if ((nint)state.L != (nint)L)
                    lua_xmove(state.L, L, 1);
                break;
            case Kind.Userdata:
                Debug.Assert(_object is CopiedUserdata);
                ((CopiedUserdata)_object).Push(state);
                if ((nint)state.L != (nint)L)
                    lua_xmove(state.L, L, 1);
                break;
            case Kind.Nil:
            default:
                lua_pushnil(L);
                break;
        }
    }

    internal void Release()
    {
        if (_type != Kind.Value)
            return;
        _value.Dispose();
    }
}
