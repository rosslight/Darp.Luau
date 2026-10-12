using Shouldly;

namespace Darp.Luau.Tests;

/// <summary> Reading a <see cref="LuauValue"/> as a managed type. Which type reads which value: <see cref="TypedReadTests"/>. </summary>
public sealed class LuauValueTests : IDisposable
{
    private readonly LuauState _state = new();

    private ulong ActiveReferences => _state.MemoryStatistics.ActiveRegistryReferences;

    private LuauValue Value(string expression) => _state.Load($"return {expression}").Execute<LuauValue>();

    [Fact]
    public void Get_ShouldReturnTheValueOrThrow()
    {
        using LuauValue text = Value("'text'");

        text.Get<string>().ShouldBe("text");
        Should.Throw<LuaGetException>(() => text.Get<int>()).Message.ShouldBe("value: number expected, got string");
    }

    [Fact]
    public void Get_ShouldRejectNilUnlessTheTypeHasAValueForIt()
    {
        LuauValue nil = Value("nil");

        Should.Throw<LuaGetException>(() => nil.Get<string>());
        Should.Throw<LuaGetException>(() => nil.Get<int>());
        nil.Get<int?>().ShouldBeNull();
        nil.Get<LuauValue>().Type.ShouldBe(LuauValueType.Nil);
    }

    [Fact]
    public void GetOrNull_ShouldReadNilAsNull()
    {
        using LuauValue text = Value("'text'");

        Value("nil").GetOrNull<string>().ShouldBeNull();
        text.GetOrNull<string>().ShouldBe("text");
        Should.Throw<LuaGetException>(() => text.GetOrNull<byte[]>());
    }

    [Fact]
    public void TryGet_ShouldNotSucceedWithNullForAReferenceType()
    {
        Value("nil").TryGet(out string? text).ShouldBeFalse();
        text.ShouldBeNull();
        Value("1").TryGet(out string? _).ShouldBeFalse();
    }

    [Fact]
    public void TryGet_ShouldReadNilAsTheNullOfANullableValueType()
    {
        Value("nil").TryGet(out int? missing).ShouldBeTrue();
        missing.ShouldBeNull();
        Value("3").TryGet(out int? three).ShouldBeTrue();
        three.ShouldBe(3);
        Value("'text'").TryGet(out int? _).ShouldBeFalse();
    }

    [Fact]
    public void TryGetOrNull_ShouldTellNilFromAWrongType()
    {
        Value("nil").TryGetOrNull(out string? missing).ShouldBeTrue();
        missing.ShouldBeNull();
        Value("1").TryGetOrNull(out string? _).ShouldBeFalse();
    }

    [Fact]
    public void TryGet_ShouldThrowForATypeNoValueCanBeReadAs()
    {
        Should.Throw<NotSupportedException>(() => Value("1").TryGet(out DateTime _));
    }

    [Fact]
    public void ValueWithoutAState_ShouldRead()
    {
        var number = (LuauValue)1.5;
        var flag = (LuauValue)true;

        number.Get<double>().ShouldBe(1.5);
        number.TryGet(out int _).ShouldBeFalse();
        flag.Get<bool>().ShouldBeTrue();
        Should.Throw<LuaGetException>(() => flag.Get<int>()).Message.ShouldBe("value: number expected, got boolean");
        default(LuauValue).Get<int?>().ShouldBeNull();
    }

    [Fact]
    public void OwnedReference_ShouldBeAReferenceOfItsOwn()
    {
        LuauValue value = Value("{ answer = 42 }");
        ulong baseline = ActiveReferences;

        LuauTable table = value.Get<LuauTable>();
        ActiveReferences.ShouldBe(baseline + 1);

        value.Dispose();
        table.GetNumber("answer").ShouldBe(42);
        table.Dispose();
        ActiveReferences.ShouldBe(baseline - 1);
    }

    [Fact]
    public void ReferenceValue_ShouldNameItsTypeWhenItIsNotWhatWasAskedFor()
    {
        using LuauValue table = Value("{}");

        Should.Throw<LuaGetException>(() => table.Get<int>()).Message.ShouldBe("value: number expected, got table");
        Should
            .Throw<LuaGetException>(() => table.Get<LuauFunction>())
            .Message.ShouldBe("value: function expected, got table");
        Should.Throw<LuaGetException>(() => table.Get<string>()).Message.ShouldBe("value: string expected, got table");
    }

    [Theory]
    [InlineData("function() end", "function")]
    [InlineData("buffer.create(1)", "buffer")]
    [InlineData("coroutine.create(function() end)", "thread")]
    [InlineData("'text'", "string")]
    public void ReferenceValue_ShouldNameEveryLuauType(string expression, string typeName)
    {
        using LuauValue value = Value(expression);

        Should
            .Throw<LuaGetException>(() => value.Get<int>())
            .Message.ShouldBe($"value: number expected, got {typeName}");
    }

    [Fact]
    public void UserdataValue_ShouldNameItsLuauTypeWhenItIsNotWhatWasAskedFor()
    {
        using LuauUserdata userdata = _state.GetOrCreateUserdata(new Fixtures.ValueUserdata());
        using LuauValue value = userdata.DisposeAndToLuauValue();

        Should.Throw<LuaGetException>(() => value.Get<int>()).Message.ShouldBe("value: number expected, got userdata");
    }

    [Fact]
    public void TryGet_ThatFails_ShouldNotAllocate()
    {
        // TryGet is how a callback chooses between the types a value may have, so most of its calls fail.
        var number = (LuauValue)1.5;
        using LuauValue table = Value("{}");
        for (int i = 0; i < 100; i++)
            Fail(number, table);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            Fail(number, table);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        allocated.ShouldBe(0);

        static void Fail(LuauValue number, LuauValue table)
        {
            number.TryGet(out string? _).ShouldBeFalse();
            number.TryGet(out int _).ShouldBeFalse();
            number.TryGet(out LuauTable _).ShouldBeFalse();
            table.TryGet(out double _).ShouldBeFalse();
            table.TryGet(out string? _).ShouldBeFalse();
        }
    }

    [Fact]
    public void Read_ShouldLeaveTheStackAsItFoundIt()
    {
        using LuauValue table = Value("{}");
        int top = _state.GetStackTop();

        table.TryGet(out int _).ShouldBeFalse();
        Should.Throw<LuaGetException>(() => table.Get<string>());
        Should.Throw<NotSupportedException>(() => table.TryGet(out DateTime _));
        table.Get<LuauTable>().Dispose();
        table.Get<LuauValue>().Dispose();

        _state.GetStackTop().ShouldBe(top);
    }

    public void Dispose() => _state.Dispose();
}
