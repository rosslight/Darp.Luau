using Shouldly;

namespace Darp.Luau.Tests;

/// <summary> Only <c>null</c> is nil. An empty string, span or array is an empty value. </summary>
public sealed class EmptyValueTests : IDisposable
{
    private readonly LuauState _state = new();

    [Fact]
    public void EmptyCharSpan_ShouldBeAnEmptyString()
    {
        using LuauTable table = _state.CreateTable();

        table.Set("key", ReadOnlySpan<char>.Empty);

        table.GetUtf8String("key").ShouldBe(string.Empty);
    }

    [Fact]
    public void EmptyString_ShouldBeAnEmptyString()
    {
        using LuauTable table = _state.CreateTable();

        table.Set("key", string.Empty);

        table.GetUtf8String("key").ShouldBe(string.Empty);
    }

    [Fact]
    public void NullString_ShouldBeNil()
    {
        using LuauTable table = _state.CreateTable();
        table.Set("key", "value");

        table.Set("key", (string?)null);

        table.ContainsKey("key").ShouldBeFalse();
    }

    [Fact]
    public void EmptyByteArray_ShouldBeAnEmptyBuffer()
    {
        using LuauTable table = _state.CreateTable();

        table.Set("key", Array.Empty<byte>());

        table.GetBuffer("key").ShouldBeEmpty();
    }

    [Fact]
    public void NullByteArray_ShouldBeNil()
    {
        using LuauTable table = _state.CreateTable();
        table.Set("key", new byte[] { 1 });

        table.Set("key", (byte[]?)null);

        table.ContainsKey("key").ShouldBeFalse();
    }

    [Fact]
    public void EmptyString_ShouldReachAManagedCallbackAsEmpty()
    {
        string? received = null;
        using LuauFunction take = _state.CreateFunction((string value) => received = value);

        take.Invoke(ReadOnlySpan<char>.Empty);

        received.ShouldBe(string.Empty);
    }

    [Fact]
    public void CreateString_ShouldAcceptEmptyText()
    {
        using LuauString fromChars = _state.CreateString(ReadOnlySpan<char>.Empty);
        using LuauString fromUtf8 = _state.CreateString(ReadOnlySpan<byte>.Empty);

        fromChars.ToString().ShouldBe(string.Empty);
        fromUtf8.ToString().ShouldBe(string.Empty);
    }

    [Fact]
    public void CreateBuffer_ShouldAcceptEmptyInput()
    {
        using LuauBuffer buffer = _state.CreateBuffer([]);

        buffer.TryGet(out byte[]? bytes).ShouldBeTrue();
        bytes.ShouldBeEmpty();
    }

    public void Dispose() => _state.Dispose();
}
