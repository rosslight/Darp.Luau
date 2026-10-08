using Shouldly;

namespace Darp.Luau.Tests;

/// <summary> Who releases a reference: every test ends with as many registry references as it started with. </summary>
public sealed class ReferenceOwnershipTests : IDisposable
{
    private readonly LuauState _state = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private ulong ActiveReferences => _state.MemoryStatistics.ActiveRegistryReferences;

    [Fact]
    public void GeneratedCallback_ShouldReleaseItsLuauValueArguments()
    {
        using LuauFunction take = _state.CreateFunction((LuauValue value) => { });
        _state.Globals.Set("take", take);
        ulong baseline = ActiveReferences;

        _state.Load("for i = 1, 100 do take({}) end").Execute();

        ActiveReferences.ShouldBe(baseline);
    }

    [Fact]
    public void GeneratedCallback_WhenALaterArgumentFails_ShouldReleaseTheOnesAlreadyRead()
    {
        bool called = false;
        using LuauFunction take = _state.CreateFunction(
            (LuauValue value, int number) =>
            {
                called = true;
            }
        );
        _state.Globals.Set("take", take);
        ulong baseline = ActiveReferences;

        _state.Load("for i = 1, 100 do pcall(take, {}, 'not a number') end").Execute();

        called.ShouldBeFalse();
        ActiveReferences.ShouldBe(baseline);
    }

    [Fact]
    public void GeneratedCallback_ThatDisposesItsArgument_ShouldStillBalance()
    {
        using LuauFunction take = _state.CreateFunction((LuauValue value) => value.Dispose());
        _state.Globals.Set("take", take);
        ulong baseline = ActiveReferences;

        _state.Load("for i = 1, 100 do take({}) end").Execute();

        ActiveReferences.ShouldBe(baseline);
    }

    [Fact]
    public void GeneratedCallback_ThatKeepsACopyOfItsArgument_ShouldOwnTheCopy()
    {
        LuauTable kept = default;
        using LuauFunction keep = _state.CreateFunction(
            (LuauValue value) =>
            {
                value.TryGet(out kept).ShouldBeTrue();
            }
        );
        _state.Globals.Set("keep", keep);

        _state.Load("keep({ answer = 42 })").Execute();

        // The argument was released when the call was over. The copy is a reference of its own.
        using (kept)
        {
            kept.GetNumber("answer").ShouldBe(42);
        }
    }

    [Fact]
    public void GeneratedCallback_ShouldHandOverTheLuauValueItReturns()
    {
        using LuauFunction make = _state.CreateFunction(() => _state.CreateTable().DisposeAndToLuauValue());
        _state.Globals.Set("make", make);
        ulong baseline = ActiveReferences;

        _state.Load("for i = 1, 100 do assert(type(make()) == 'table') end").Execute();

        ActiveReferences.ShouldBe(baseline);
    }

    [Fact]
    public void GeneratedCallback_ShouldHandOverLuauValuesInATuple()
    {
        using LuauFunction make = _state.CreateFunction(() =>
            (_state.CreateTable().DisposeAndToLuauValue(), 5, _state.CreateTable().DisposeAndToLuauValue())
        );
        _state.Globals.Set("make", make);
        ulong baseline = ActiveReferences;

        _state
            .Load(
                """
                for i = 1, 100 do
                    local first, number, second = make()
                    assert(type(first) == 'table' and number == 5 and type(second) == 'table')
                end
                """
            )
            .Execute();

        ActiveReferences.ShouldBe(baseline);
    }

    [Fact]
    public async Task GeneratedAsyncCallback_ShouldKeepItsLuauValueArgumentsUntilItsWorkCompletes()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using LuauFunction count = _state.CreateFunction(
            async (LuauValue value) =>
            {
                await gate.Task;
                // The argument is still alive after the await.
                if (!value.TryGet(out LuauTable table))
                    return -1;
                using (table)
                    return table.ListCount;
            }
        );
        _state.Globals.Set("count", count);
        ulong baseline = ActiveReferences;

        ValueTask<int> pending = _state.Load("return count({ 1, 2, 3 })").ExecuteAsync<int>([], TestToken);
        pending.IsCompleted.ShouldBeFalse();
        gate.SetResult();

        (await pending).ShouldBe(3);
        ActiveReferences.ShouldBe(baseline);
    }

    [Fact]
    public async Task GeneratedAsyncCallback_WhenItsWorkFails_ShouldReleaseItsArguments()
    {
        using LuauFunction fail = _state.CreateFunction(
            async (LuauValue value) =>
            {
                await Task.Yield();
                throw new InvalidOperationException("boom");
            }
        );
        _state.Globals.Set("fail", fail);
        ulong baseline = ActiveReferences;

        bool ok = await _state.Load("return (pcall(fail, {}))").ExecuteAsync<bool>([], TestToken);

        ok.ShouldBeFalse();
        ActiveReferences.ShouldBe(baseline);
    }

    [Fact]
    public void GeneratedAsyncCallback_WhenItCannotAwait_ShouldReleaseItsArguments()
    {
        bool called = false;
        using LuauFunction wait = _state.CreateFunction(
            async (LuauValue value) =>
            {
                called = true;
                await Task.Yield();
            }
        );
        _state.Globals.Set("wait", wait);
        ulong baseline = ActiveReferences;

        // A sync host call cannot await, so the callback is refused before it is called.
        _state.Load("for i = 1, 100 do pcall(wait, {}) end").Execute();

        called.ShouldBeFalse();
        ActiveReferences.ShouldBe(baseline);
    }

    [Fact]
    public async Task GeneratedAsyncCallback_ShouldHandOverTheLuauValueItReturns()
    {
        using LuauFunction make = _state.CreateFunction(async () =>
        {
            await Task.Yield();
            return _state.CreateTable().DisposeAndToLuauValue();
        });
        _state.Globals.Set("make", make);
        ulong baseline = ActiveReferences;

        string type = await _state.Load("return type(make())").ExecuteAsync<string>([], TestToken);

        type.ShouldBe("table");
        ActiveReferences.ShouldBe(baseline);
    }

    [Fact]
    public void GeneratedUserdataMethod_ShouldReleaseItsArgumentAndHandOverItsResult()
    {
        _state.Globals.Set("echo", IntoLuau.FromUserdata(new GeneratedEcho()));
        ulong baseline = ActiveReferences;

        _state.Load("for i = 1, 100 do local t = {} assert(echo:copy(t) == t) end").Execute();

        ActiveReferences.ShouldBe(baseline);
    }

    [Fact]
    public void TypedResults_WhenALaterResultCannotBeRead_ShouldReleaseTheOnesAlreadyRead()
    {
        ulong baseline = ActiveReferences;

        Should.Throw<InvalidCastException>(() => _state.Load("return {}, 'text'").Execute<LuauTable, int>());
        Should.Throw<InvalidCastException>(() =>
            _state.Load("return {}, {}, {}, 'text'").Execute<LuauTable, LuauTable, LuauTable, int>()
        );

        ActiveReferences.ShouldBe(baseline);
    }

    [Fact]
    public void DisposedLuauValue_ShouldNotBeCopied()
    {
        LuauValue value = _state.CreateTable().DisposeAndToLuauValue();
        value.Dispose();

        value.TryGet(out LuauValue _).ShouldBeFalse();
    }

    [Fact]
    public void TableEnumeration_ShouldSurviveChangesToTheTable()
    {
        using LuauTable table = _state.Load("return { a = 1, b = 2, c = 3 }").Execute<LuauTable>();
        ulong baseline = ActiveReferences;
        int visited = 0;

        foreach (KeyValuePair<LuauValue, LuauValue> pair in table)
        {
            using LuauValue key = pair.Key;
            using LuauValue value = pair.Value;
            visited++;
            // Removing the current key and growing the table makes Luau rebuild it under the enumeration.
            table.Set(key, default);
            if (visited == 1)
            {
                for (int i = 0; i < 64; i++)
                    table.Set($"added{i}", i);
            }
            if (visited > 1000)
                break;
        }

        visited.ShouldBeGreaterThan(0);
        ActiveReferences.ShouldBe(baseline);
    }

    [Fact]
    public void CopiedTableEnumerator_ShouldHaveNothingToReleaseTwice()
    {
        using LuauTable table = _state.Load("return { a = 1, b = 2 }").Execute<LuauTable>();
        ulong baseline = ActiveReferences;

        LuauTable.Enumerator enumerator = table.GetEnumerator();
        enumerator.MoveNext().ShouldBeTrue();
        enumerator.Current.Key.Dispose();
        enumerator.Current.Value.Dispose();
        LuauTable.Enumerator copy = enumerator;
        ((IDisposable)copy).Dispose();
        ((IDisposable)enumerator).Dispose();

        ActiveReferences.ShouldBe(baseline);
        // The registry is intact: new references still resolve to what they were created for.
        using LuauTable other = _state.Load("return { marker = 7 }").Execute<LuauTable>();
        other.GetNumber("marker").ShouldBe(7);
    }

    [Fact]
    public void TableExtensionTryGet_ShouldRefuseSpans()
    {
        _state.Globals.Set("text", "hello");

        Should.Throw<NotSupportedException>(() => _state.Globals.TryGet("text", out ReadOnlySpan<byte> _));

        LuauMarshal.TryGetUtf8StringSpan(_state.Globals, "text", out ReadOnlySpan<byte> bytes).ShouldBeTrue();
        bytes.SequenceEqual("hello"u8).ShouldBeTrue();
    }

    public void Dispose() => _state.Dispose();
}

[LuauUserdata]
public sealed partial class GeneratedEcho
{
    /// <summary> Returns a reference of its own to the value it was given. </summary>
    [LuauMember("copy")]
    public LuauValue Copy(LuauValue value) => value.TryGet(out LuauValue copy) ? copy : default;
}
