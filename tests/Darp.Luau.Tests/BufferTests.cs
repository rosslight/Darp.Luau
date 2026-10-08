using Shouldly;

namespace Darp.Luau.Tests;

public sealed class BufferTests : IDisposable
{
    private readonly LuauState _state = new();

    [Fact]
    public void Create_And_Get()
    {
        byte[] expected = [0x01, 0x02, 0x03];

        using LuauBuffer buffer = _state.CreateBuffer(expected);

        buffer.TryGet(out byte[]? found).ShouldBeTrue();
        found.ShouldNotBeNull();
        found.ShouldBe<byte>(expected);
    }

    [Fact]
    public void TryGet_Bytes()
    {
        byte[] expected = [0x01, 0x02, 0x03];

        using LuauBuffer buffer = _state.CreateBuffer(expected);

        using LuauValue value = buffer.DisposeAndToLuauValue();
        value.Type.ShouldBe(LuauValueType.Buffer);

        value.TryGet(out byte[]? found).ShouldBeTrue();
        found.ShouldBe<byte>(expected);
    }

    [Fact]
    public void TryGet_Span()
    {
        ReadOnlySpan<byte> expected = new([0x01, 0x02, 0x03]);

        using LuauBuffer buffer = _state.CreateBuffer(expected);

        using LuauValue value = buffer.DisposeAndToLuauValue();
        value.Type.ShouldBe(LuauValueType.Buffer);

        value.TryGet(out LuauBuffer owned).ShouldBeTrue();
        using (owned)
        {
            owned.TryGet(out ReadOnlySpan<byte> found).ShouldBeTrue();
            found.ToArray().ShouldBe<byte>(expected.ToArray());
        }
    }

    [Fact]
    public void TryGet_Buffer()
    {
        using LuauBuffer expected = _state.CreateBuffer([0x01, 0x02, 0x03]);

        using LuauValue value = expected.DisposeAndToLuauValue();
        value.Type.ShouldBe(LuauValueType.Buffer);

        value.TryGet(out LuauBuffer found).ShouldBeTrue();
        using (found)
        {
            found.TryGet(out byte[]? bytes).ShouldBeTrue();
            bytes.ShouldBe<byte>([0x01, 0x02, 0x03]);
        }
    }

    [Fact]
    public void To_String()
    {
        byte[] expected = [0x01, 0x02, 0x03];

        using LuauBuffer buffer = _state.CreateBuffer(expected);

        buffer.ToString().ShouldBeEquivalentTo(Convert.ToHexString(expected));
    }

    public void Dispose()
    {
        _state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(1UL);
        _state.Dispose();
    }
}
