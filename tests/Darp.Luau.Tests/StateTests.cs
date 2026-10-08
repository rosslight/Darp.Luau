using Shouldly;

namespace Darp.Luau.Tests;

public sealed class StateTests : IDisposable
{
    private readonly LuauState _state = new();

    [Fact]
    public void DoString_ShouldReturnVoid_U8()
    {
        _state.Load("result = 42"u8).Execute();

        _state.Globals.GetNumber("result").ShouldBe(42);
    }

    [Fact]
    public void DoString_ShouldReturnVoid()
    {
        _state.Load("result = 42").Execute();

        _state.Globals.GetNumber("result").ShouldBe(42);
    }

    [Fact]
    public void DoString_ShouldReturnTypedValue_U8()
    {
        int result = _state.Load("return 42"u8).Execute<int>();

        result.ShouldBe(42);
    }

    [Fact]
    public void DoString_ShouldReturnTypedValue()
    {
        int result = _state.Load("return 42").Execute<int>();

        result.ShouldBe(42);
    }

    [Fact]
    public void DoString_TupleReturn_ShouldIgnoreAdditionalReturnValues_U8()
    {
        (byte first, short second) = _state.Load("return 10, 11, 12"u8).Execute<byte, short>();

        first.ShouldBe<byte>(10);
        second.ShouldBe<short>(11);
    }

    [Fact]
    public void DoString_TupleReturn_ShouldIgnoreAdditionalReturnValues()
    {
        (byte first, short second) = _state.Load("return 10, 11, 12").Execute<byte, short>();

        first.ShouldBe<byte>(10);
        second.ShouldBe<short>(11);
    }

    [Fact]
    public void DoString_TupleReturn_WithOwnedReference_ShouldCloneReferenceOwnership()
    {
        (LuauTable table, int count) = _state.Load("return { value = 42 }, 5").Execute<LuauTable, int>();
        using (table)
        {
            count.ShouldBe(5);
            table.GetNumber("value").ShouldBe(42);
        }
    }

    [Fact]
    public void DoString_ShouldReturnTypedTuple()
    {
        (int number, string? text, bool flag) = _state.Load("return 10, 'hello', true").Execute<int, string?, bool>();

        number.ShouldBe(10);
        text.ShouldBe("hello");
        flag.ShouldBeTrue();
    }

    [Fact]
    public void DoString_ShouldReturnTypedTuple4_U8()
    {
        (int number, string? nilText, string text, bool flag) = _state
            .Load("return 10, nil, 'hello', true"u8)
            .Execute<int, string?, string, bool>();

        number.ShouldBe(10);
        nilText.ShouldBeNull();
        text.ShouldBe("hello");
        flag.ShouldBeTrue();
    }

    [Fact]
    public void DoString_ShouldReturnTypedTuple4()
    {
        (int number, string? nilText, string text, bool flag) = _state
            .Load("return 10, nil, 'hello', true")
            .Execute<int, string?, string, bool>();

        number.ShouldBe(10);
        nilText.ShouldBeNull();
        text.ShouldBe("hello");
        flag.ShouldBeTrue();
    }

    [Fact]
    public void DoString_TupleReturn_ShouldThrowWhenMissingValueCannotBeConverted()
    {
        Should.Throw<InvalidCastException>(() =>
        {
            LuauChunk chunk = _state.Load("return 1");
            _ = chunk.Execute<int, int>();
        });
    }

    [Fact]
    public void DoStringMulti_ShouldReturnEmptyArray_WhenChunkReturnsNothing()
    {
        LuauValue[] values = _state.Load("return").ExecuteMulti();

        values.ShouldBeEmpty();
    }

    [Fact]
    public void DoStringMulti_ShouldReadAllReturnValues()
    {
        LuauValue[] values = _state.Load("return 10, 'hello', true").ExecuteMulti();
        values.Length.ShouldBe(3);

        using LuauValue value1 = values[0];
        using LuauValue value2 = values[1];
        using LuauValue value3 = values[2];

        value1.TryGet(out int number, acceptNil: false).ShouldBeTrue();
        value2.TryGet(out string? text, acceptNil: false).ShouldBeTrue();
        value3.TryGet(out bool flag, acceptNil: false).ShouldBeTrue();

        number.ShouldBe(10);
        text.ShouldBe("hello");
        flag.ShouldBeTrue();
    }

    [Fact]
    public void DoStringMulti_WithOwnedReference_ShouldCloneReferenceOwnership()
    {
        LuauValue[] values = _state.Load("return { value = 42 }, 5").ExecuteMulti();
        values.Length.ShouldBe(2);

        using LuauValue tableValue = values[0];
        using LuauValue countValue = values[1];

        ulong beforeClone = _state.MemoryStatistics.ActiveRegistryReferences;
        tableValue.TryGet(out LuauTable table).ShouldBeTrue();
        using (table)
        {
            _state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(beforeClone + 1);
            table.GetNumber("value").ShouldBe(42);
        }
        _state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(beforeClone);

        countValue.TryGet(out int count, acceptNil: false).ShouldBeTrue();
        count.ShouldBe(5);
    }

    [Fact]
    public void DoStringMulti_ByteSpanOverload_ShouldMatchCharSpanOverload()
    {
        LuauValue[] values = _state.Load("return 1, 2"u8).ExecuteMulti();
        values.Length.ShouldBe(2);

        using LuauValue value1 = values[0];
        using LuauValue value2 = values[1];

        value1.TryGet(out int first, acceptNil: false).ShouldBeTrue();
        value2.TryGet(out int second, acceptNil: false).ShouldBeTrue();

        first.ShouldBe(1);
        second.ShouldBe(2);
    }

    [Fact]
    public void DoStringMulti_ShouldThrowLuaException_WhenChunkErrors()
    {
        LuaException exception = Should.Throw<LuaException>(() =>
        {
            LuauChunk chunk = _state.Load("error('boom')");
            _ = chunk.ExecuteMulti();
        });

        exception.Message.ShouldContain("boom");
    }

    [Fact]
    public void Dispose_InsideACallback_ShouldThrowAndLeaveTheStateUsable()
    {
        using var state = new LuauState();
        using LuauFunction quit = state.CreateFunctionBuilder(_ =>
        {
            state.Dispose();
            return LuauReturn.Ok();
        });
        state.Globals.Set("quit", quit);

        // Luau would continue in a closed state when the callback returns.
        LuaException exception = Should.Throw<LuaException>(() => state.Load("quit()").Execute());

        exception.Message.ShouldContain("cannot be disposed while one of its managed callbacks runs");
        state.IsDisposed.ShouldBeFalse();
        state.Load("return 1 + 1").Execute<int>().ShouldBe(2);
    }

    [Fact]
    public void Dispose_InsideAUserdataCallback_ShouldThrow()
    {
        using var state = new LuauState();
        state.Globals.Set("disposer", IntoLuau.FromUserdata(new DisposingUserdata(state)));

        Should.Throw<LuaException>(() => state.Load("return disposer.anything").Execute());
        Should.Throw<LuaException>(() => state.Load("disposer.anything = 1").Execute());
        Should.Throw<LuaException>(() => state.Load("disposer:anything()").Execute());

        state.IsDisposed.ShouldBeFalse();
    }

    [Fact]
    public void Dispose_AfterACallbackFailed_ShouldWork()
    {
        var state = new LuauState();
        using LuauFunction fail = state.CreateFunctionBuilder(_ => throw new InvalidOperationException("boom"));
        state.Globals.Set("fail", fail);
        Should.Throw<LuaException>(() => state.Load("fail()").Execute());

        state.Dispose();

        state.IsDisposed.ShouldBeTrue();
    }

    [Fact]
    public void CreateString_WithLongText_ShouldRoundTrip()
    {
        using var state = new LuauState();
        string text = new('ä', 100_000);

        using LuauString value = state.CreateString(text);
        state.Globals.Set("text", value);

        state.Load("return #text").Execute<int>().ShouldBe(200_000);
        state.Globals.TryGet("text", out string? roundTripped).ShouldBeTrue();
        roundTripped.ShouldBe(text);
    }

    private sealed class DisposingUserdata(LuauState state) : ILuauUserData<DisposingUserdata>
    {
        private readonly LuauState _state = state;

        public static LuauReturnSingle OnIndex(
            DisposingUserdata self,
            in LuauState state,
            in ReadOnlySpan<char> fieldName
        )
        {
            self._state.Dispose();
            return LuauReturnSingle.NotHandled;
        }

        public static LuauOutcome OnSetIndex(
            DisposingUserdata self,
            LuauArgsSingle args,
            in ReadOnlySpan<char> fieldName
        )
        {
            self._state.Dispose();
            return LuauOutcome.NotHandledError;
        }

        public static LuauReturn OnMethodCall(
            DisposingUserdata self,
            LuauArgs functionArgs,
            in ReadOnlySpan<char> methodName
        )
        {
            self._state.Dispose();
            return LuauReturn.NotHandledError;
        }
    }

    public void Dispose()
    {
        _state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(1U);
        _state.Dispose();
    }
}
