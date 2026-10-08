using System.Buffers;
using System.Text;

namespace Darp.Luau.Internal;

/// <summary> Text encoded as UTF-8, on the stack when it is short and in a pooled array otherwise. </summary>
internal ref struct Utf8Buffer
{
    public const int StackSize = 256;

    private byte[]? _rented;

    /// <param name="text">The text to encode.</param>
    /// <param name="stackBuffer">Holds the bytes of short text.</param>
    public Utf8Buffer(ReadOnlySpan<char> text, Span<byte> stackBuffer)
    {
        Span<byte> buffer =
            Encoding.UTF8.GetMaxByteCount(text.Length) <= stackBuffer.Length
                ? stackBuffer
                : _rented = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetByteCount(text));
        Bytes = buffer[..Encoding.UTF8.GetBytes(text, buffer)];
    }

    public ReadOnlySpan<byte> Bytes { get; }

    public void Dispose()
    {
        if (_rented is not null)
            ArrayPool<byte>.Shared.Return(_rented);
        _rented = null;
    }
}

/// <summary> UTF-8 text decoded to chars, on the stack when it is short and in a pooled array otherwise. </summary>
/// <remarks> Scripts choose the length of the names they index a userdata with, so it must not size a stack buffer. </remarks>
internal ref struct Utf16Buffer
{
    public const int StackSize = 128;

    private char[]? _rented;

    /// <param name="utf8Text">The text to decode.</param>
    /// <param name="stackBuffer">Holds the chars of short text.</param>
    public Utf16Buffer(ReadOnlySpan<byte> utf8Text, Span<char> stackBuffer)
    {
        // Text never decodes to more chars than it has bytes.
        Span<char> buffer =
            utf8Text.Length <= stackBuffer.Length
                ? stackBuffer
                : _rented = ArrayPool<char>.Shared.Rent(utf8Text.Length);
        Chars = buffer[..Encoding.UTF8.GetChars(utf8Text, buffer)];
    }

    public ReadOnlySpan<char> Chars { get; }

    public void Dispose()
    {
        if (_rented is not null)
            ArrayPool<char>.Shared.Return(_rented);
        _rented = null;
    }
}
