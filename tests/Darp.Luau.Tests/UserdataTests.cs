using Shouldly;

namespace Darp.Luau.Tests;

public sealed class UserdataTests
{
    [Fact]
    public void Userdata_IndexSetterAndNamecall_ShouldWorkUnderNone()
    {
        using var state = new LuauState(LuauLibraries.None);
        var counter = new CounterUserdata();
        CounterUserdata.LastMethodName = null;
        CounterUserdata.LastParameterCount = -1;

        state.Globals.Set("counter", counter);
        state
            .Load(
                """
                before = counter.value
                counter.value = 41
                after = counter.value
                methodResult = counter:add(1)
                """
            )
            .Execute();

        state.Globals.GetNumber("before").ShouldBe(0);
        state.Globals.GetNumber("after").ShouldBe(41);
        state.Globals.GetNumber("methodResult").ShouldBe(42);
        CounterUserdata.LastMethodName.ShouldBe("add");
        CounterUserdata.LastParameterCount.ShouldBe(1);
    }

    [Fact]
    public void Userdata_CallbackError_ShouldThrowUnderNone()
    {
        using var state = new LuauState(LuauLibraries.None);
        state.Globals.Set("failing", new FailingUserdata());

        LuaException exception = Should.Throw<LuaException>(() => state.Load("return failing.explode").Execute());

        exception.Message.ShouldContain("__index callback failed");
        exception.Message.ShouldContain("Boom from OnIndex");
    }

    [Fact]
    public void Userdata_CallbacksFromCoroutine_ShouldWork()
    {
        using var state = new LuauState();
        CounterUserdata.LastMethodName = null;
        CounterUserdata.LastParameterCount = -1;
        state.Globals.Set("counter", new CounterUserdata());

        (int before, int after, int methodResult) = state
            .Load(
                """
                local co = coroutine.create(function()
                  local before = counter.value
                  counter.value = 41
                  local after = counter.value
                  return before, after, counter:add(1)
                end)
                local ok, before, after, methodResult = coroutine.resume(co)
                if not ok then error(before) end
                return before, after, methodResult
                """
            )
            .Execute<int, int, int>();

        before.ShouldBe(0);
        after.ShouldBe(41);
        methodResult.ShouldBe(42);
        CounterUserdata.LastMethodName.ShouldBe("add");
        CounterUserdata.LastParameterCount.ShouldBe(1);
    }

    [Theory]
    [InlineData("return failing.explode", "__index callback failed", "Boom from OnIndex")]
    [InlineData("failing.explodeSet = 1", "__newindex callback failed", "Boom from OnSetIndex")]
    [InlineData("return failing:explodeMethod()", "__namecall callback failed", "Boom from OnMethodCall")]
    public void Userdata_CallbackErrorsFromCoroutine_ShouldBeLuaErrors(
        string callbackSource,
        string expectedCallback,
        string expectedMessage
    )
    {
        using var state = new LuauState();
        state.Globals.Set("failing", new FailingUserdata());

        (bool ok, string error) = state
            .Load(
                $$"""
                local co = coroutine.create(function()
                  {{callbackSource}}
                end)
                local ok, value = coroutine.resume(co)
                return ok, tostring(value)
                """
            )
            .Execute<bool, string>();

        ok.ShouldBeFalse();
        error.ShouldContain(expectedCallback);
        error.ShouldContain(expectedMessage);
    }

    [Fact]
    public void Userdata_IndexSetterAndMethodCall_ShouldWork()
    {
        using var state = new LuauState();
        var counter = new CounterUserdata();
        CounterUserdata.LastMethodName = null;
        CounterUserdata.LastParameterCount = -1;

        state.Globals.Set("counter", counter);
        state
            .Load(
                """
                before = counter.value
                counter.value = 41
                after = counter.value
                methodResult = counter:add(1)
                """
            )
            .Execute();

        state.Globals.GetNumber("before").ShouldBe(0);

        state.Globals.TryGet("after", out int after).ShouldBeTrue();
        after.ShouldBe(41);

        CounterUserdata.LastMethodName.ShouldBe("add");
        CounterUserdata.LastParameterCount.ShouldBe(1);

        state.Globals.TryGet("methodResult", out LuauValue methodResultValue).ShouldBeTrue();
        methodResultValue.Type.ShouldBe(LuauValueType.Number);
        methodResultValue.TryGet(out int methodResult).ShouldBeTrue();
        methodResult.ShouldBe(42);
    }

    [Fact]
    public void Userdata_MethodCallViaColonSyntax_ShouldWork()
    {
        using var state = new LuauState();
        CounterUserdata.LastMethodName = null;
        CounterUserdata.LastParameterCount = -1;
        state.Globals.Set("counter", new CounterUserdata());

        state
            .Load(
                """
                counter.value = 41
                methodResult = counter:add(1)
                """
            )
            .Execute();

        state.Globals.TryGet("methodResult", out int methodResult).ShouldBeTrue();
        methodResult.ShouldBe(42);
        CounterUserdata.LastMethodName.ShouldBe("add");
        CounterUserdata.LastParameterCount.ShouldBe(1);
    }

    [Fact]
    public void Userdata_ShouldRoundtripThroughLuauValue()
    {
        using var state = new LuauState();
        var counter = new CounterUserdata();

        using LuauUserdata userdata = state.GetOrCreateUserdata(counter);
        state.Globals.Set("userdata", userdata);

        state.Globals.TryGet("userdata", out LuauValue value).ShouldBeTrue();
        value.Type.ShouldBe(LuauValueType.Userdata);
        value.TryGet(out LuauUserdata roundtrip).ShouldBeTrue();

        using (roundtrip)
        {
            state.Globals.Set("userdataFromValue", value);
            state.Load("isUserdataPresent = userdataFromValue ~= nil").Execute();
            state.Globals.TryGet("isUserdataPresent", out bool isUserdataPresent).ShouldBeTrue();
            isUserdataPresent.ShouldBeTrue();
        }
    }

    [Fact]
    public void Userdata_GetOrCreate_WithSameManagedInstance_ShouldReuseLuaIdentity()
    {
        using var state = new LuauState();
        var counter = new CounterUserdata();

        using LuauUserdata first = state.GetOrCreateUserdata(counter);
        using LuauUserdata second = state.GetOrCreateUserdata(counter);

        state.Globals.Set("first", first);
        state.Globals.Set("second", second);
        state.Load("sameIdentity = first == second").Execute();

        state.Globals.TryGet("sameIdentity", out bool sameIdentity).ShouldBeTrue();
        sameIdentity.ShouldBeTrue();
    }

    [Fact]
    public void Userdata_LuauUserdata_TryGetManaged_ShouldResolveManagedInstance()
    {
        using var state = new LuauState();
        var counter = new CounterUserdata();

        using LuauUserdata userdata = state.GetOrCreateUserdata(counter);
        userdata.TryGetManaged(out CounterUserdata? resolved, out string? error).ShouldBeTrue(error);

        ReferenceEquals(counter, resolved).ShouldBeTrue();
    }

    [Fact]
    public void Userdata_TableGetUserdata_ShouldResolveManagedInstance()
    {
        using var state = new LuauState();
        var counter = new CounterUserdata();
        LuauTable table = state.CreateTable();

        table.Set("counter", counter);

        CounterUserdata resolved = table.GetUserdata<CounterUserdata>("counter");
        ReferenceEquals(counter, resolved).ShouldBeTrue();

        using LuauUserdata reference = table.GetLuauUserdata("counter");
        reference.TryGetManaged(out CounterUserdata? resolvedFromReference, out string? error).ShouldBeTrue(error);
        ReferenceEquals(counter, resolvedFromReference).ShouldBeTrue();
    }

    [Fact]
    public void Userdata_TableTryGetUserdata_WithWrongManagedType_ReturnsFalse()
    {
        using var state = new LuauState();
        LuauTable table = state.CreateTable();
        table.Set("failing", new FailingUserdata());

        table.TryGetUserdata<CounterUserdata>("failing", out _).ShouldBeFalse();
    }

    [Fact]
    public void Userdata_UnknownIndex_ShouldReturnNil()
    {
        using var state = new LuauState();
        state.Globals.Set("counter", new CounterUserdata());

        state.Load("missingValue = counter.missing").Execute();

        state.Globals.TryGet("missingValue", out LuauValue missingValue).ShouldBeTrue();
        missingValue.Type.ShouldBe(LuauValueType.Nil);
    }

    [Fact]
    public void Userdata_NonStringIndexKey_ShouldRaiseLuaException()
    {
        using var state = new LuauState();
        state.Globals.Set("counter", new CounterUserdata());

        LuaException exception = Should.Throw<LuaException>(() =>
        {
            LuauChunk chunk = state.Load("x = counter[1]");
            chunk.Execute();
        });

        exception.Message.ShouldContain("userdata index access requires a string member name");
    }

    [Fact]
    public void Userdata_UnknownSet_ShouldRaiseLuaException()
    {
        using var state = new LuauState();
        state.Globals.Set("counter", new CounterUserdata());

        LuaException exception = Should.Throw<LuaException>(() =>
        {
            LuauChunk chunk = state.Load("counter.missing = 5");
            chunk.Execute();
        });

        exception.Message.ShouldContain("unknown userdata member 'missing'");
    }

    [Fact]
    public void Userdata_UnknownSet_ShouldBeCatchableByPCall()
    {
        using var state = new LuauState();
        state.Globals.Set("counter", new CounterUserdata());

        state
            .Load(
                """
                ok, err = pcall(function()
                  counter.missing = 5
                end)
                """
            )
            .Execute();

        state.Globals.TryGet("ok", out bool ok).ShouldBeTrue();
        ok.ShouldBeFalse();

        state.Globals.TryGet("err", out string? err).ShouldBeTrue();
        err.ShouldContain("unknown userdata member 'missing'");
    }

    [Fact]
    public void Userdata_NonStringSetKey_ShouldBeCatchableByPCall()
    {
        using var state = new LuauState();
        state.Globals.Set("counter", new CounterUserdata());

        state
            .Load(
                """
                ok, err = pcall(function()
                  counter[1] = 5
                end)
                """
            )
            .Execute();

        state.Globals.TryGet("ok", out bool ok).ShouldBeTrue();
        ok.ShouldBeFalse();

        state.Globals.TryGet("err", out string? err).ShouldBeTrue();
        err.ShouldContain("userdata assignment requires a string member name");
    }

    [Fact]
    public void Userdata_UnknownMethod_ShouldRaiseLuaException()
    {
        using var state = new LuauState();
        state.Globals.Set("counter", new CounterUserdata());

        LuaException exception = Should.Throw<LuaException>(() =>
            state.Load("result = counter:missingMethod(1)").Execute()
        );

        exception.Message.ShouldContain("unknown userdata method 'missingMethod'");
    }

    [Fact]
    public void Userdata_UnknownMethod_ShouldBeCatchableByPCall()
    {
        using var state = new LuauState();
        state.Globals.Set("counter", new CounterUserdata());

        state
            .Load(
                """
                ok, err = pcall(function()
                  return counter:missingMethod(1)
                end)
                """
            )
            .Execute();

        state.Globals.TryGet("ok", out bool ok).ShouldBeTrue();
        ok.ShouldBeFalse();

        state.Globals.TryGet("err", out string? err).ShouldBeTrue();
        err.ShouldContain("unknown userdata method 'missingMethod'");
    }

    [Fact]
    public void Userdata_Metatable_ShouldBeHiddenFromScripts()
    {
        using var state = new LuauState();
        state.Globals.Set("counter", new CounterUserdata());

        // Every userdata of a state shares one metatable. A script that could change it would change them all.
        state
            .Load(
                """
                locked = getmetatable(counter)
                canSet = pcall(setmetatable, counter, {})
                counter.value = 5
                after = counter:add(1)
                """
            )
            .Execute();

        state.Globals.TryGet("locked", out string? locked).ShouldBeTrue();
        locked.ShouldBe("The metatable is locked");
        state.Globals.TryGet("canSet", out bool canSet).ShouldBeTrue();
        canSet.ShouldBeFalse();
        state.Globals.TryGet("after", out int after).ShouldBeTrue();
        after.ShouldBe(6);
    }

    [Fact]
    public void Userdata_HugeMemberName_ShouldNotOverflowTheStack()
    {
        using var state = new LuauState();
        state.Globals.Set("counter", new CounterUserdata());

        // The script chooses how long the name is, so it must not size a stack buffer.
        state
            .Load(
                """
                local name = string.rep("x", 4 * 1024 * 1024)
                missing = counter[name]
                setOk = pcall(function() counter[name] = 1 end)
                counter.value = 2
                after = counter.value
                """
            )
            .Execute();
        state.Load($"callOk = pcall(function() return counter:{new string('x', 100_000)}() end)").Execute();

        state.Globals.TryGet("missing", out LuauValue missing).ShouldBeTrue();
        missing.Type.ShouldBe(LuauValueType.Nil);
        state.Globals.TryGet("setOk", out bool setOk).ShouldBeTrue();
        setOk.ShouldBeFalse();
        state.Globals.TryGet("callOk", out bool callOk).ShouldBeTrue();
        callOk.ShouldBeFalse();
        state.Globals.TryGet("after", out int after).ShouldBeTrue();
        after.ShouldBe(2);
    }

    [Fact]
    public void Userdata_CallbackException_ShouldTranslateToLuaException()
    {
        using var state = new LuauState();
        state.Globals.Set("failing", new FailingUserdata());

        LuaException exception = Should.Throw<LuaException>(() =>
        {
            LuauChunk chunk = state.Load("x = failing.explode");
            chunk.Execute();
        });

        exception.Message.ShouldContain("__index callback failed");
        exception.Message.ShouldContain("Boom from OnIndex");
    }

    [Fact]
    public void Userdata_CallbackException_ShouldBeCatchableByPCall()
    {
        using var state = new LuauState();
        state.Globals.Set("failing", new FailingUserdata());

        state
            .Load(
                """
                ok, err = pcall(function()
                  return failing.explode
                end)
                """
            )
            .Execute();

        state.Globals.TryGet("ok", out bool ok).ShouldBeTrue();
        ok.ShouldBeFalse();

        state.Globals.TryGet("err", out string? err).ShouldBeTrue();
        err.ShouldContain("__index callback failed");
        err.ShouldContain("Boom from OnIndex");
    }

    [Fact]
    public void Userdata_IndexErrorResult_ShouldBeCatchableByPCall()
    {
        using var state = new LuauState();
        state.Globals.Set("failing", new FailingUserdata());

        state
            .Load(
                """
                ok, err = pcall(function()
                  return failing.errorIndex
                end)
                """
            )
            .Execute();

        state.Globals.TryGet("ok", out bool ok).ShouldBeTrue();
        ok.ShouldBeFalse();

        state.Globals.TryGet("err", out string? err).ShouldBeTrue();
        err.ShouldContain("error from OnIndex");
        err.ShouldNotContain("__index callback failed");
    }

    [Fact]
    public void Userdata_SetterCallbackException_ShouldTranslateToLuaException()
    {
        using var state = new LuauState();
        state.Globals.Set("failing", new FailingUserdata());

        LuaException exception = Should.Throw<LuaException>(() =>
        {
            LuauChunk chunk = state.Load("failing.explodeSet = 1");
            chunk.Execute();
        });

        exception.Message.ShouldContain("__newindex callback failed");
        exception.Message.ShouldContain("Boom from OnSetIndex");
    }

    [Fact]
    public void Userdata_SetterCallbackException_ShouldBeCatchableByPCall()
    {
        using var state = new LuauState();
        state.Globals.Set("failing", new FailingUserdata());

        state
            .Load(
                """
                ok, err = pcall(function()
                  failing.explodeSet = 1
                end)
                """
            )
            .Execute();

        state.Globals.TryGet("ok", out bool ok).ShouldBeTrue();
        ok.ShouldBeFalse();

        state.Globals.TryGet("err", out string? err).ShouldBeTrue();
        err.ShouldContain("__newindex callback failed");
        err.ShouldContain("Boom from OnSetIndex");
    }

    [Fact]
    public void Userdata_SetterErrorResult_ShouldBeCatchableByPCall()
    {
        using var state = new LuauState();
        state.Globals.Set("failing", new FailingUserdata());

        state
            .Load(
                """
                ok, err = pcall(function()
                  failing.errorSet = 1
                end)
                """
            )
            .Execute();

        state.Globals.TryGet("ok", out bool ok).ShouldBeTrue();
        ok.ShouldBeFalse();

        state.Globals.TryGet("err", out string? err).ShouldBeTrue();
        err.ShouldContain("error from OnSetIndex");
        err.ShouldNotContain("__newindex callback failed");
    }

    [Fact]
    public void Userdata_MethodCallbackException_ShouldTranslateToLuaException()
    {
        using var state = new LuauState();
        state.Globals.Set("failing", new FailingUserdata());

        LuaException exception = Should.Throw<LuaException>(() => state.Load("x = failing:explodeMethod()").Execute());

        exception.Message.ShouldContain("__namecall callback failed");
        exception.Message.ShouldContain("Boom from OnMethodCall");
    }

    [Fact]
    public void Userdata_MethodCallbackException_ShouldBeCatchableByPCall()
    {
        using var state = new LuauState();
        state.Globals.Set("failing", new FailingUserdata());

        state
            .Load(
                """
                ok, err = pcall(function()
                  return failing:explodeMethod()
                end)
                """
            )
            .Execute();

        state.Globals.TryGet("ok", out bool ok).ShouldBeTrue();
        ok.ShouldBeFalse();

        state.Globals.TryGet("err", out string? err).ShouldBeTrue();
        err.ShouldContain("__namecall callback failed");
        err.ShouldContain("Boom from OnMethodCall");
    }

    [Fact]
    public void Userdata_MethodCanReturnMultipleValues()
    {
        using var state = new LuauState();
        state.Globals.Set("counter", new CounterUserdata());

        state
            .Load(
                """
                counter.value = 10
                first, second = counter:pair()
                """
            )
            .Execute();

        state.Globals.TryGet("first", out int first).ShouldBeTrue();
        first.ShouldBe(10);

        state.Globals.TryGet("second", out int second).ShouldBeTrue();
        second.ShouldBe(11);
    }

    [Fact]
    public void Userdata_MethodCanReturnNoValues()
    {
        using var state = new LuauState();
        state.Globals.Set("counter", new CounterUserdata());

        state
            .Load(
                """
                counter.value = 10
                noResult = counter:touch()
                after = counter.value
                """
            )
            .Execute();

        state.Globals.TryGet("noResult", out LuauValue noResult).ShouldBeTrue();
        noResult.Type.ShouldBe(LuauValueType.Nil);

        state.Globals.TryGet("after", out int after).ShouldBeTrue();
        after.ShouldBe(11);
    }

    [Fact]
    public void Userdata_UnhandledMethodThatPushes_ShouldStillErrorAndStayUsable()
    {
        using var state = new LuauState();
        state.Globals.Set("counter", new CounterUserdata());

        state
            .Load(
                """
                ok, err = pcall(function()
                  return counter:badUnknown()
                end)
                after = counter:add(1)
                """
            )
            .Execute();

        state.Globals.TryGet("ok", out bool ok).ShouldBeTrue();
        ok.ShouldBeFalse();

        state.Globals.TryGet("err", out string? err).ShouldBeTrue();
        err.ShouldContain("unknown userdata method 'badUnknown'");

        state.Globals.TryGet("after", out int after).ShouldBeTrue();
        after.ShouldBe(1);
    }

    [Fact]
    public void Userdata_RepeatedPCallFailure_ShouldStayStable()
    {
        using var state = new LuauState();
        state.Globals.Set("failing", new FailingUserdata());

        state
            .Load(
                """
                failures = 0
                for i = 1, 2000 do
                  local ok = pcall(function()
                    failing.explodeSet = i
                  end)
                  if not ok then
                    failures = failures + 1
                  end
                end
                """
            )
            .Execute();

        state.Globals.TryGet("failures", out int failures).ShouldBeTrue();
        failures.ShouldBe(2000);
    }

    private sealed class CounterUserdata : ILuauUserData<CounterUserdata>
    {
        public static string? LastMethodName { get; set; }
        public static int LastParameterCount { get; set; }

        public int Value { get; private set; }

        public static LuauReturnSingle OnIndex(
            CounterUserdata self,
            in LuauState state,
            in ReadOnlySpan<char> fieldName
        )
        {
            return fieldName switch
            {
                "value" => LuauReturnSingle.Ok(self.Value),
                _ => LuauReturnSingle.NotHandled,
            };
        }

        public static LuauOutcome OnSetIndex(CounterUserdata self, LuauArgsSingle args, in ReadOnlySpan<char> fieldName)
        {
            switch (fieldName)
            {
                case "value":
                {
                    if (!args.TryReadNumber(out int value, out string? error))
                        return LuauOutcome.Error(error);
                    self.Value = value;
                    return LuauOutcome.Ok();
                }
                default:
                    return LuauOutcome.NotHandledError;
            }
        }

        public static LuauReturn OnMethodCall(
            CounterUserdata self,
            LuauArgs functionArgs,
            in ReadOnlySpan<char> methodName
        )
        {
            LastMethodName = methodName.ToString();
            LastParameterCount = functionArgs.ArgumentCount;

            switch (methodName)
            {
                case "add":
                {
                    if (functionArgs.ArgumentCount != 1)
                        return LuauReturn.Error($"expected 1 arguments, got {functionArgs.ArgumentCount}");
                    if (!functionArgs.TryReadNumber(1, out int offset, out string? error))
                        return LuauReturn.Error(error);

                    return LuauReturn.Ok(self.Value + offset);
                }
                case "pair":
                {
                    if (functionArgs.ArgumentCount != 0)
                        return LuauReturn.Error($"expected 0 arguments, got {functionArgs.ArgumentCount}");

                    return LuauReturn.Ok(self.Value, self.Value + 1);
                }
                case "touch":
                {
                    if (functionArgs.ArgumentCount != 0)
                        return LuauReturn.Error($"expected 0 arguments, got {functionArgs.ArgumentCount}");

                    self.Value++;
                    return LuauReturn.Ok();
                }
                case "badUnknown":
                    return LuauReturn.NotHandledError;
                default:
                    return LuauReturn.NotHandledError;
            }
        }

        public static implicit operator IntoLuau(CounterUserdata value) => IntoLuau.FromUserdata(value);
    }

    private sealed class FailingUserdata : ILuauUserData<FailingUserdata>
    {
        public static LuauReturnSingle OnIndex(
            FailingUserdata self,
            in LuauState state,
            in ReadOnlySpan<char> fieldName
        )
        {
            return fieldName switch
            {
                "explode" => throw new InvalidOperationException("Boom from OnIndex"),
                "errorIndex" => LuauReturnSingle.Error("error from OnIndex"),
                _ => LuauReturnSingle.NotHandled,
            };
        }

        public static LuauOutcome OnSetIndex(FailingUserdata self, LuauArgsSingle args, in ReadOnlySpan<char> fieldName)
        {
            return fieldName switch
            {
                "explodeSet" => throw new InvalidOperationException("Boom from OnSetIndex"),
                "errorSet" => LuauOutcome.Error("error from OnSetIndex"),
                _ => LuauOutcome.NotHandledError,
            };
        }

        public static LuauReturn OnMethodCall(
            FailingUserdata self,
            LuauArgs functionArgs,
            in ReadOnlySpan<char> methodName
        )
        {
            return methodName switch
            {
                "explodeMethod" => throw new InvalidOperationException("Boom from OnMethodCall"),
                _ => LuauReturn.NotHandledError,
            };
        }

        public static implicit operator IntoLuau(FailingUserdata value) => IntoLuau.FromUserdata(value);
    }
}
