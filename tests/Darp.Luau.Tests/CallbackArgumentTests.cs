using System.Text;
using Darp.Luau.Tests.Fixtures;
using Shouldly;

namespace Darp.Luau.Tests;

/// <summary> Reading the arguments of a managed callback. Which type reads which value: <see cref="TypedReadTests"/>. </summary>
public sealed class CallbackArgumentTests : IDisposable
{
    private readonly LuauState _state = new();

    /// <summary> Calls <paramref name="callback"/> as <c>f</c> from <paramref name="script"/> and returns the error of the script. </summary>
    private string ErrorOf(string script, LuauCallback callback)
    {
        using LuauFunction function = _state.CreateFunctionManual(callback);
        _state.Globals.Set("f", function);
        return _state.Load($"local ok, err = pcall(function() {script} end) return tostring(err)").Execute<string>()
            ?? string.Empty;
    }

    private T? Call<T>(string script, LuauCallback callback)
    {
        using LuauFunction function = _state.CreateFunctionManual(callback);
        _state.Globals.Set("f", function);
        return _state.Load(script).Execute<T>();
    }

    [Fact]
    public void Get_ShouldReadTheArgument()
    {
        Call<int>("return f(40, 2)", static args => LuauReturn.Ok(args[1].Get<int>() + args[2].Get<int>()))
            .ShouldBe(42);
    }

    [Fact]
    public void Get_WithAnotherType_ShouldRaiseALuauErrorThatNamesTheArgument()
    {
        ErrorOf("f(1, 'two')", static args => LuauReturn.Ok(args[1].Get<int>() + args[2].Get<int>()))
            .ShouldBe("invalid argument #2 (number expected, got string)");
    }

    [Fact]
    public void Get_WithANumberTheTypeCannotHold_ShouldSayWhichNumber()
    {
        ErrorOf("f(1.5)", static args => LuauReturn.Ok(args[1].Get<int>()))
            .ShouldBe("invalid argument #1 (number that fits Int32 expected, got 1.5)");
    }

    [Fact]
    public void Get_WithoutTheArgument_ShouldSayThatItIsMissing()
    {
        ErrorOf("f(1)", static args => LuauReturn.Ok(args[1].Get<int>() + args[2].Get<int>()))
            .ShouldBe("missing argument #2 (number expected)");
    }

    [Fact]
    public void Get_WithNil_ShouldSayWhatWasPassed()
    {
        ErrorOf("f(nil)", static args => LuauReturn.Ok(args[1].Get<string>()))
            .ShouldBe("invalid argument #1 (string expected, got nil)");
    }

    [Fact]
    public void OmittedArgument_ShouldReadAsNil()
    {
        Call<string>(
                "return f()",
                static args =>
                    LuauReturn.Ok(
                        string.Join(
                            ' ',
                            args.Count,
                            args[1].Type,
                            args[1].TypeName,
                            args[1].Get<int?>() is null,
                            args[1].GetOrNull<string>() is null,
                            args[1].TryGetOrNull(out string? text) && text is null,
                            args[1].TryGet(out int _),
                            args[1].Get<LuauValue>().Type
                        )
                    )
            )
            .ShouldBe("0 Nil nil True True True False Nil");
    }

    [Fact]
    public void OmittedArgument_ShouldStayNilWhenAValueIsPushedWhereItWouldBe()
    {
        // The table is the only argument. Setting a key pushes it into the slot a second argument would have.
        Call<string>(
                "local t = {} f(t) return tostring(t.k)",
                static args =>
                {
                    args[1].Get<LuauTableView>().Set("k", args[2]);
                    return LuauReturn.Ok();
                }
            )
            .ShouldBe("nil");
    }

    [Fact]
    public void Index_BelowOne_ShouldBeAnErrorOfTheHost()
    {
        ErrorOf("f(1)", static args => LuauReturn.Ok(args[0].Get<int>())).ShouldContain("ArgumentOutOfRangeException");
    }

    [Fact]
    public void TryGet_ShouldChooseBetweenTheTypesAnArgumentMayHave()
    {
        LuauCallback describe = static args =>
            args[1].TryGet(out double number) ? LuauReturn.Ok($"number {number}")
            : args[1].TryGet(out string? text) ? LuauReturn.Ok($"string {text}")
            : args[1].TryGet(out ValueUserdata? userdata) ? LuauReturn.Ok($"userdata {userdata.Value}")
            : LuauReturn.Ok(args[1].TypeName);
        _state.Globals.Set("input", new ValueUserdata { Value = 7 });

        Call<string>("return f(2)", describe).ShouldBe("number 2");
        Call<string>("return f('a')", describe).ShouldBe("string a");
        Call<string>("return f(input)", describe).ShouldBe("userdata 7");
        Call<string>("return f({})", describe).ShouldBe("table");
    }

    [Fact]
    public void Get_ShouldReadBorrowedViewsAndSpans()
    {
        Call<string>(
                "return f('héllo', buffer.fromstring('abc'), { 1, 2, 3 }, function() return 'called' end)",
                static args =>
                {
                    string text = Encoding.UTF8.GetString(args[1].Get<ReadOnlySpan<byte>>());
                    args[1].Get<LuauStringView>().TryGet(out string? sameText).ShouldBeTrue();
                    args[2].Get<LuauBufferView>().TryGet(out byte[] bytes).ShouldBeTrue();
                    int count = args[3].Get<LuauTableView>().ListCount;
                    string? result = args[4].Get<LuauFunctionView>().Invoke<string>();
                    return LuauReturn.Ok($"{text} {sameText} {Encoding.UTF8.GetString(bytes)} {count} {result}");
                }
            )
            .ShouldBe("héllo héllo abc 3 called");
    }

    [Fact]
    public void Get_AsABorrowedViewOfAnotherType_ShouldRaiseALuauError()
    {
        ErrorOf("f(1)", static args => Use(args[1].Get<LuauTableView>().ListCount))
            .ShouldBe("invalid argument #1 (table expected, got number)");
        ErrorOf("f(1)", static args => Use(args[1].Get<LuauFunctionView>().ToString()))
            .ShouldBe("invalid argument #1 (function expected, got number)");
        ErrorOf("f(1)", static args => Use(args[1].Get<LuauStringView>().ToString()))
            .ShouldBe("invalid argument #1 (string expected, got number)");
        ErrorOf("f(1)", static args => Use(args[1].Get<LuauBufferView>().ToString()))
            .ShouldBe("invalid argument #1 (buffer expected, got number)");
        ErrorOf("f(1)", static args => Use(args[1].Get<LuauUserdataView>().ToString()))
            .ShouldBe("invalid argument #1 (userdata expected, got number)");
        ErrorOf("f(1)", static args => Use(args[1].Get<ReadOnlySpan<byte>>().Length))
            .ShouldBe("invalid argument #1 (string expected, got number)");
        ErrorOf("f()", static args => Use(args[1].Get<LuauTableView>().ListCount))
            .ShouldBe("missing argument #1 (table expected)");

        static LuauReturn Use<T>(T value) => LuauReturn.Ok(value?.ToString());
    }

    [Fact]
    public void Get_ShouldReadABorrowedUserdataView()
    {
        var userdata = new ValueUserdata { Value = 7 };
        _state.Globals.Set("input", userdata);

        Call<bool>(
                "return f(input)",
                args =>
                {
                    args[1].Get<LuauUserdataView>().TryGetManaged(out ValueUserdata? managed, out _).ShouldBeTrue();
                    return LuauReturn.Ok(ReferenceEquals(managed, userdata));
                }
            )
            .ShouldBeTrue();
    }

    [Fact]
    public void GetOrNull_ShouldOnlyTakeNilInPlaceOfTheValue()
    {
        Call<string>("return f('text')", static args => LuauReturn.Ok(args[1].GetOrNull<string>())).ShouldBe("text");
        ErrorOf("f(1)", static args => LuauReturn.Ok(args[1].GetOrNull<string>()))
            .ShouldBe("invalid argument #1 (string expected, got number)");
    }

    [Fact]
    public void TryGetOrNull_ShouldTellNilFromAnotherType()
    {
        LuauCallback describe = static args =>
            LuauReturn.Ok(args[1].TryGetOrNull(out string? text) ? text ?? "nil" : "another type");

        Call<string>("return f('text')", describe).ShouldBe("text");
        Call<string>("return f(nil)", describe).ShouldBe("nil");
        Call<string>("return f(1)", describe).ShouldBe("another type");
    }

    [Fact]
    public void TypeNoValueCanBeReadAs_ShouldBeAnErrorOfTheHost()
    {
        ErrorOf("f(1)", static args => LuauReturn.Ok(args[1].Get<DateTime>().Ticks))
            .ShouldContain("NotSupportedException");
        ErrorOf("f(1)", static args => LuauReturn.Ok(args[1].TryGet(out DateTime _)))
            .ShouldContain("NotSupportedException");
    }

    [Fact]
    public void Get_ShouldReadAnOwnedReferenceThatOutlivesTheCallback()
    {
        LuauTable stored = default;
        Call<int>(
            "f({ answer = 42 }) return 0",
            args =>
            {
                stored = args[1].Get<LuauTable>();
                return LuauReturn.Ok();
            }
        );

        using (stored)
            stored.GetNumber("answer").ShouldBe(42);
    }

    [Fact]
    public void Argument_ShouldBePassedOnWithoutReadingIt()
    {
        Call<string>(
                "local t = {} return tostring(f(t) == t) .. ' ' .. tostring(f())",
                static args => LuauReturn.Ok(args[1])
            )
            .ShouldBe("true nil");
    }

    [Fact]
    public void Arguments_PassedOnTogether_ShouldReleaseTheFirstWhenTheSecondHasNoManagedForm()
    {
        using LuauFunction function = _state.CreateFunctionManual(static args => LuauReturn.Ok(args[1], args[2]));
        _state.Globals.Set("f", function);
        ulong baseline = _state.MemoryStatistics.ActiveRegistryReferences;

        // A vector cannot be captured for the return. The table before it was captured already.
        _state.Load("for i = 1, 100 do pcall(f, {}, vector.create(1, 2, 3)) end").Execute();

        _state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(baseline);
    }

    [Fact]
    public void TryGet_ShouldReadABorrowedView()
    {
        Call<string>(
                "return f({ 1, 2 }) .. ' ' .. f(1)",
                static args =>
                    LuauReturn.Ok(args[1].TryGet(out LuauTableView table) ? $"table of {table.ListCount}" : "no table")
            )
            .ShouldBe("table of 2 no table");
    }

    [Fact]
    public void Argument_ShouldReadTheSameAfterTheCallbackCalledBackIntoLuau()
    {
        Call<string>(
                "return f('first', function(...) return select('#', ...) end, 'third')",
                static args =>
                {
                    int passed = args[2].Get<LuauFunctionView>().Invoke<int>(1, 2, 3, 4);
                    return LuauReturn.Ok($"{args[1].Get<string>()} {passed} {args[3].Get<string>()} {args[4].Type}");
                }
            )
            .ShouldBe("first 4 third Nil");
    }

    [Fact]
    public void FailedRead_InsideACallback_ShouldBeTheErrorOfTheScript()
    {
        // Not only arguments: every read that fails is reported as it is, without the name of the exception.
        ErrorOf(
                "f('text')",
                static args =>
                {
                    using LuauValue value = args[1].Get<LuauValue>();
                    return LuauReturn.Ok(value.Get<int>());
                }
            )
            .ShouldBe("value: number expected, got string");
    }

    [Fact]
    public void AssignedValue_WithAnotherType_ShouldRaiseALuauError()
    {
        _state.Globals.Set("counter", new SetterUserdata());

        string? error = _state
            .Load("local ok, err = pcall(function() counter.value = 'text' end) return tostring(err)")
            .Execute<string>();

        error.ShouldBe("invalid value (number expected, got string)");
    }

    [Fact]
    public void GeneratedCallback_ShouldPassNullForAnOmittedNullableParameter()
    {
        using LuauFunction number = _state.CreateFunction((int? value) => value ?? -1);
        using LuauFunction text = _state.CreateFunction((string? value) => value ?? "none");
        using LuauFunction userdata = _state.CreateFunction((ValueUserdata? value) => value is null);
        using LuauFunction second = _state.CreateFunction((int first, int? second) => first + (second ?? 100));
        _state.Globals.Set("number", number);
        _state.Globals.Set("text", text);
        _state.Globals.Set("userdata", userdata);
        _state.Globals.Set("second", second);

        _state.Load("return number()").Execute<int>().ShouldBe(-1);
        _state.Load("return number(nil)").Execute<int>().ShouldBe(-1);
        _state.Load("return number(5)").Execute<int>().ShouldBe(5);
        _state.Load("return text()").Execute<string>().ShouldBe("none");
        _state.Load("return userdata()").Execute<bool>().ShouldBeTrue();
        _state.Load("return second(1)").Execute<int>().ShouldBe(101);
        _state.Load("return second(1, 2)").Execute<int>().ShouldBe(3);
    }

    [Fact]
    public void GeneratedCallback_ShouldRaiseALuauErrorForAMissingRequiredParameter()
    {
        bool called = false;
        using LuauFunction add = _state.CreateFunction(
            (int a, int b) =>
            {
                called = true;
                return a + b;
            }
        );
        _state.Globals.Set("add", add);

        string? error = _state.Load("local ok, err = pcall(add, 1) return tostring(err)").Execute<string>();

        error.ShouldBe("missing argument #2 (number expected)");
        called.ShouldBeFalse();
    }

    [Fact]
    public void GeneratedCallback_ShouldIgnoreArgumentsItHasNoParameterFor()
    {
        using LuauFunction add = _state.CreateFunction((int a, int b) => a + b);
        _state.Globals.Set("add", add);

        _state.Load("return add(1, 2, 3, 'four')").Execute<int>().ShouldBe(3);
    }

    public void Dispose() => _state.Dispose();

    private sealed class SetterUserdata : ILuauUserdata<SetterUserdata>
    {
        private int _value;

        public static void Register(LuauUserdataRegistry<SetterUserdata> registry) =>
            registry.AddSetter(
                "value",
                static (self, value) =>
                {
                    self._value = value.Get<int>();
                    return LuauOutcome.Ok();
                }
            );

        public static implicit operator IntoLuau(SetterUserdata value) => IntoLuau.FromUserdata(value);
    }
}
