using Shouldly;

namespace Darp.Luau.Tests;

public sealed class FunctionTests : IDisposable
{
    private readonly LuauState _state = new();

    [Fact]
    public void Simple()
    {
        _state
            .Load(
                """
                function get_value(message: string)
                  return message;
                end
                """
            )
            .Execute();
        _ = _state.Globals.TryGet("get_value", out LuauFunction func);
        using (func)
        {
            string r = func.Invoke<string>("Message");
            r.ShouldBe("Message");
        }
    }

    [Fact]
    public void CSharpFunction_ShouldBeCalled()
    {
        string? messageToLog = null;

        using LuauFunction func = _state.CreateFunction(Log);
        _state.Globals.Set("log", func);

        _state.Load("""log("hello from lua")""").Execute();

        messageToLog.ShouldBe("hello from lua");
        return;

        void Log(string message) => messageToLog = message;
    }

    [Fact]
    public void LuaFunction_ShouldBeCalled()
    {
        _state
            .Load(
                """
                function add(a, b)
                 return a + b
                end
                """
            )
            .Execute();

        using LuauFunction luaFunc = _state.Globals.GetLuauFunction("add");

        luaFunc.Invoke<int>(1, 2).ShouldBe(3);
    }

    [Fact]
    public void CSharpFunction_Adder_ShouldBeCalled()
    {
        using LuauFunction func = _state.CreateFunction(Add);
        _state.Globals.Set("add", func);

        _state.Load("result = add(1, 2)").Execute();
        _state.Globals.TryGet("result", out int result).ShouldBeTrue();

        result.ShouldBe(3);
        return;

        int Add(int p1, int p2) => p1 + p2;
    }

    [Fact]
    public void CSharpFunction_Exception_ShouldBeCatchableByPCall()
    {
        using LuauFunction func = _state.CreateFunctionBuilder(static _ =>
            throw new InvalidOperationException("Boom from managed function")
        );
        _state.Globals.Set("explode", func);

        _state.Load("ok, err = pcall(explode)").Execute();

        _state.Globals.TryGet("ok", out bool ok).ShouldBeTrue();
        ok.ShouldBeFalse();

        _state.Globals.TryGet("err", out string? err).ShouldBeTrue();
        err.ShouldContain("managed function callback failed");
        err.ShouldContain("Boom from managed function");
    }

    [Fact]
    public void CSharpFunction_Exception_ShouldBeCatchable()
    {
        using LuauFunction func = _state.CreateFunctionBuilder(static _ =>
            throw new InvalidOperationException("Boom from managed function")
        );
        _state.Globals.Set("explode", func);

        LuaException exception = Should.Throw<LuaException>(() =>
        {
            LuauChunk chunk = _state.Load("explode();");
            chunk.Execute();
        });
        exception.Message.ShouldContain("managed function callback failed");
        exception.Message.ShouldContain("Boom from managed function");
    }

    [Fact]
    public void CSharpFunction_ResultObject_ShouldReturnValue()
    {
        using LuauFunction func = _state.CreateFunctionBuilder(static args =>
        {
            if (!args.TryReadNumber(1, out int a, out string? error) || !args.TryReadNumber(2, out int b, out error))
                return LuauReturn.Error(error);

            return LuauReturn.Ok(a + b);
        });
        _state.Globals.Set("add", func);

        _state.Load("result = add(1, 2)").Execute();
        _state.Globals.TryGet("result", out int result).ShouldBeTrue();
        result.ShouldBe(3);
    }

    [Fact]
    public void CSharpFunction_ResultObject_ShouldReturnErrorString()
    {
        using LuauFunction func = _state.CreateFunctionBuilder(static _ => LuauReturn.Error("user facing error"));
        _state.Globals.Set("fail", func);

        _state.Load("ok, err = pcall(fail)").Execute();

        _state.Globals.TryGet("ok", out bool ok).ShouldBeTrue();
        ok.ShouldBeFalse();

        _state.Globals.TryGet("err", out string? err).ShouldBeTrue();
        err.ShouldContain("user facing error");
    }

    [Fact]
    public void CSharpFunction_ResultObject_FromCoroutine_ShouldReturnValue()
    {
        using LuauFunction func = _state.CreateFunctionBuilder(static args =>
        {
            if (!args.TryReadNumber(1, out int a, out string? error) || !args.TryReadNumber(2, out int b, out error))
                return LuauReturn.Error(error);

            return LuauReturn.Ok(a + b);
        });
        _state.Globals.Set("add", func);

        int result = _state
            .Load(
                """
                local co = coroutine.create(function()
                  return add(40, 2)
                end)
                local ok, value = coroutine.resume(co)
                if not ok then error(value) end
                return value
                """
            )
            .Execute<int>();

        result.ShouldBe(42);
    }

    [Fact]
    public void CSharpFunction_ResultObject_ErrorFromCoroutine_ShouldBeLuaError()
    {
        using LuauFunction func = _state.CreateFunctionBuilder(static _ => LuauReturn.Error("boom from coroutine"));
        _state.Globals.Set("fail", func);

        (bool ok, string error) = _state
            .Load(
                """
                local co = coroutine.create(function()
                  return fail()
                end)
                local ok, value = coroutine.resume(co)
                return ok, tostring(value)
                """
            )
            .Execute<bool, string>();

        ok.ShouldBeFalse();
        error.ShouldContain("boom from coroutine");
    }

    [Fact]
    public void CSharpFunction_ResultObject_ShouldReturnStringValueViaOk()
    {
        using LuauFunction func = _state.CreateFunctionBuilder(static _ => LuauReturn.Ok("hello from csharp"));
        _state.Globals.Set("greet", func);

        _state.Load("result = greet()").Execute();

        _state.Globals.TryGet("result", out string? result).ShouldBeTrue();
        result.ShouldBe("hello from csharp");
    }

    [Fact]
    public void CSharpFunction_ResultObject_ImplicitString_ShouldBeAnError()
    {
        using LuauFunction func = _state.CreateFunctionBuilder(static _ => LuauReturn.Error("error from callback"));
        _state.Globals.Set("fail", func);

        _state.Load("ok, err = pcall(fail)").Execute();

        _state.Globals.TryGet("ok", out bool ok).ShouldBeTrue();
        ok.ShouldBeFalse();

        _state.Globals.TryGet("err", out string? err).ShouldBeTrue();
        err.ShouldContain("error from callback");
    }

    [Fact]
    public void CSharpFunction_ResultObject_ShouldSupportNoReturnValues()
    {
        using LuauFunction func = _state.CreateFunctionBuilder(static _ => LuauReturn.Ok());
        _state.Globals.Set("touch", func);

        _state.Load("returnCount = select('#', touch())").Execute();

        _state.Globals.TryGet("returnCount", out int returnCount).ShouldBeTrue();
        returnCount.ShouldBe(0);
    }

    [Fact]
    public void CSharpFunction_ResultObject_ShouldSupportMultipleReturnValues()
    {
        using LuauFunction func = _state.CreateFunctionBuilder(static _ => LuauReturn.Ok(10, 11));
        _state.Globals.Set("pair", func);

        _state
            .Load(
                """
                first, second = pair()
                returnCount = select('#', pair())
                """
            )
            .Execute();

        _state.Globals.TryGet("first", out int first).ShouldBeTrue();
        first.ShouldBe(10);

        _state.Globals.TryGet("second", out int second).ShouldBeTrue();
        second.ShouldBe(11);

        _state.Globals.TryGet("returnCount", out int returnCount).ShouldBeTrue();
        returnCount.ShouldBe(2);
    }

    [Fact]
    public void Invoke_ScalarReturn_ShouldIgnoreAdditionalReturnValues()
    {
        using LuauFunction func = _state.CreateFunctionBuilder(static _ => LuauReturn.Ok(10, 11));

        func.Invoke<int>().ShouldBe(10);
    }

    [Fact]
    public void Invoke_TupleReturn_ShouldReadMultipleReturnValues()
    {
        using LuauFunction func = _state.CreateFunctionBuilder(static _ => LuauReturn.Ok(10, "hello", true));

        (int number, string? text, bool flag) = func.Invoke<int, string?, bool>();

        number.ShouldBe(10);
        text.ShouldBe("hello");
        flag.ShouldBeTrue();
    }

    [Fact]
    public void Invoke_TupleReturn_WithOwnedReference_ShouldCloneReferenceOwnership()
    {
        using LuauFunction func = _state.CreateFunctionBuilder(args =>
        {
            if (!args.TryValidateArgumentCount(0, out string? error))
                return LuauReturn.Error(error);

            using LuauTable table = _state.CreateTable();
            table.Set("value", 42);
            return LuauReturn.Ok(table, 5);
        });

        ulong baselineActiveReferences = _state.MemoryStatistics.ActiveRegistryReferences;

        (LuauTable table, int count) = func.Invoke<LuauTable, int>();
        using (table)
        {
            count.ShouldBe(5);
            table.GetNumber("value").ShouldBe(42);
        }

        _state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(baselineActiveReferences);
    }

    [Fact]
    public void Invoke_TupleReturn_ShouldIgnoreAdditionalReturnValues()
    {
        using LuauFunction func = _state.CreateFunctionBuilder(static _ => LuauReturn.Ok(10, 11, 12));

        (byte first, short second) = func.Invoke<byte, short>();
        first.ShouldBe<byte>(10);
        second.ShouldBe<short>(11);
    }

    [Fact]
    public void Invoke_Void_ShouldIgnoreReturnValues()
    {
        using LuauFunction func = _state.CreateFunctionBuilder(static _ => LuauReturn.Ok(10, 11));

        func.Invoke();
    }

    [Fact]
    public void InvokeMulti_ShouldReadAllReturnValues()
    {
        using LuauFunction func = _state.CreateFunctionBuilder(static _ => LuauReturn.Ok(10, "hello", true));

        LuauValue[] values = func.InvokeMulti();
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
    public void Func_NoArgs_Returns()
    {
        using LuauFunction f1 = _state.CreateFunction(() => true);
        using LuauFunction f2 = _state.CreateFunction(() => (sbyte)1);
        using LuauFunction f3 = _state.CreateFunction(() => (byte)1);
        using LuauFunction f4 = _state.CreateFunction(() => (short)1);
        using LuauFunction f5 = _state.CreateFunction(() => (ushort)1);
        using LuauFunction f6 = _state.CreateFunction(() => 1);
        using LuauFunction f7 = _state.CreateFunction(() => (uint)1);
        using LuauFunction f8 = _state.CreateFunction(() => (long)1);
        using LuauFunction f9 = _state.CreateFunction(() => (ulong)1);
        using LuauFunction f10 = _state.CreateFunction(() => (Int128)1);
        using LuauFunction f11 = _state.CreateFunction(() => (UInt128)1);
        using LuauFunction f12 = _state.CreateFunction(() => "1");
        using LuauFunction f13 = _state.CreateFunction(() => (Half)1);
        using LuauFunction f14 = _state.CreateFunction(() => (float)1);
        using LuauFunction f15 = _state.CreateFunction(() => (double)1);
        using LuauFunction f16 = _state.CreateFunction(() => (decimal)1);

        f1.Invoke<bool>().ShouldBe(true);
        f2.Invoke<sbyte>().ShouldBe((sbyte)1);
        f3.Invoke<byte>().ShouldBe((byte)1);
        f4.Invoke<short>().ShouldBe((short)1);
        f5.Invoke<ushort>().ShouldBe((ushort)1);
        f6.Invoke<int>().ShouldBe(1);
        f7.Invoke<uint>().ShouldBe((uint)1);
        f8.Invoke<long>().ShouldBe(1);
        f9.Invoke<ulong>().ShouldBe((ulong)1);
        f10.Invoke<Int128>().ShouldBe(1);
        f11.Invoke<UInt128>().ShouldBe((UInt128)1);
        f12.Invoke<string>().ShouldBe("1");
        f13.Invoke<Half>().ShouldBe((Half)1);
        f14.Invoke<float>().ShouldBe(1);
        f15.Invoke<double>().ShouldBe(1);
        f16.Invoke<decimal>().ShouldBe(1);
    }

    [Fact]
    public void Func_OneArg_Returns()
    {
        using LuauFunction f1 = _state.CreateFunction((bool x) => x);
        using LuauFunction f2 = _state.CreateFunction((sbyte x) => x);
        using LuauFunction f3 = _state.CreateFunction((byte x) => x);
        using LuauFunction f4 = _state.CreateFunction((short x) => x);
        using LuauFunction f5 = _state.CreateFunction((ushort x) => x);
        using LuauFunction f6 = _state.CreateFunction((int x) => x);
        using LuauFunction f7 = _state.CreateFunction((uint x) => x);
        using LuauFunction f8 = _state.CreateFunction((long x) => x);
        using LuauFunction f9 = _state.CreateFunction((ulong x) => x);
        using LuauFunction f10 = _state.CreateFunction((Int128 x) => x);
        using LuauFunction f11 = _state.CreateFunction((UInt128 x) => x);
        using LuauFunction f12 = _state.CreateFunction((string x) => x);
        using LuauFunction f13 = _state.CreateFunction((Half x) => x);
        using LuauFunction f14 = _state.CreateFunction((float x) => x);
        using LuauFunction f15 = _state.CreateFunction((double x) => x);
        using LuauFunction f16 = _state.CreateFunction((decimal x) => x);

        f1.Invoke<bool>(true).ShouldBe(true);
        f2.Invoke<sbyte>(1).ShouldBe((sbyte)1);
        f3.Invoke<byte>(1).ShouldBe((byte)1);
        f4.Invoke<short>(1).ShouldBe((short)1);
        f5.Invoke<ushort>(1).ShouldBe((ushort)1);
        f6.Invoke<int>(1).ShouldBe(1);
        f7.Invoke<uint>(1).ShouldBe((uint)1);
        f8.Invoke<long>(1).ShouldBe(1);
        f9.Invoke<ulong>(1).ShouldBe((ulong)1);
        f10.Invoke<Int128>(1).ShouldBe(1);
        f11.Invoke<UInt128>(1).ShouldBe((UInt128)1);
        f12.Invoke<string>("1").ShouldBe("1");
        f13.Invoke<Half>(1).ShouldBe((Half)1);
        f14.Invoke<float>(1).ShouldBe(1);
        f15.Invoke<double>(1).ShouldBe(1);
        f16.Invoke<decimal>(1).ShouldBe(1);
    }

    [Fact]
    public void Func_OneArgNullable_Returns()
    {
        using LuauFunction f1 = _state.CreateFunction((bool? x) => x);
        using LuauFunction f2 = _state.CreateFunction((sbyte? x) => x);
        using LuauFunction f3 = _state.CreateFunction((byte? x) => x);
        using LuauFunction f4 = _state.CreateFunction((short? x) => x);
        using LuauFunction f5 = _state.CreateFunction((ushort? x) => x);
        using LuauFunction f6 = _state.CreateFunction((int? x) => x);
        using LuauFunction f7 = _state.CreateFunction((uint? x) => x);
        using LuauFunction f8 = _state.CreateFunction((long? x) => x);
        using LuauFunction f9 = _state.CreateFunction((ulong? x) => x);
        using LuauFunction f10 = _state.CreateFunction((Int128? x) => x);
        using LuauFunction f11 = _state.CreateFunction((UInt128? x) => x);
        using LuauFunction f12 = _state.CreateFunction((string? x) => x);
        using LuauFunction f13 = _state.CreateFunction((Half? x) => x);
        using LuauFunction f14 = _state.CreateFunction((float? x) => x);
        using LuauFunction f15 = _state.CreateFunction((double? x) => x);
        using LuauFunction f16 = _state.CreateFunction((decimal? x) => x);

        f1.Invoke<bool?>((bool?)null).ShouldBe(null);
        f2.Invoke<sbyte?>((sbyte?)null).ShouldBe(null);
        f3.Invoke<byte?>((byte?)null).ShouldBe(null);
        f4.Invoke<short?>((short?)null).ShouldBe(null);
        f5.Invoke<ushort?>((ushort?)null).ShouldBe(null);
        f6.Invoke<int?>((int?)null).ShouldBe(null);
        f7.Invoke<uint?>((uint?)null).ShouldBe(null);
        f8.Invoke<long?>((long?)null).ShouldBe(null);
        f9.Invoke<ulong?>((ulong?)null).ShouldBe(null);
        f10.Invoke<Int128?>((long?)null).ShouldBe(null);
        f11.Invoke<UInt128?>((ulong?)null).ShouldBe(null);
        f12.Invoke<string?>((string?)null).ShouldBe(null);
        f13.Invoke<Half?>((Half?)null).ShouldBe(null);
        f14.Invoke<float?>((float?)null).ShouldBe(null);
        f15.Invoke<double?>((double?)null).ShouldBe(null);
        f16.Invoke<decimal?>((double?)null).ShouldBe(null);
    }

    [Fact]
    public void Func_TwoArgs_Returns()
    {
        using LuauFunction f1 = _state.CreateFunction((bool x1, bool x2) => (x1, x2));
        using LuauFunction f2 = _state.CreateFunction((sbyte x1, sbyte x2) => (x1, x2));
        using LuauFunction f3 = _state.CreateFunction((byte x1, byte x2) => (x1, x2));
        using LuauFunction f4 = _state.CreateFunction((short x1, short x2) => (x1, x2));
        using LuauFunction f5 = _state.CreateFunction((ushort x1, ushort x2) => (x1, x2));
        using LuauFunction f6 = _state.CreateFunction((int x1, int x2) => (x1, x2));
        using LuauFunction f7 = _state.CreateFunction((uint x1, uint x2) => (x1, x2));
        using LuauFunction f8 = _state.CreateFunction((long x1, long x2) => (x1, x2));
        using LuauFunction f9 = _state.CreateFunction((ulong x1, ulong x2) => (x1, x2));
        using LuauFunction f10 = _state.CreateFunction((Int128 x1, Int128 x2) => (x1, x2));
        using LuauFunction f11 = _state.CreateFunction((UInt128 x1, UInt128 x2) => (x1, x2));
        using LuauFunction f12 = _state.CreateFunction((string x1, string x2) => (x1, x2));
        using LuauFunction f13 = _state.CreateFunction((Half x1, Half x2) => (x1, x2));
        using LuauFunction f14 = _state.CreateFunction((float x1, float x2) => (x1, x2));
        using LuauFunction f15 = _state.CreateFunction((double x1, double x2) => (x1, x2));
        using LuauFunction f16 = _state.CreateFunction((decimal x1, decimal x2) => (x1, x2));

        f1.Invoke<bool, bool>(true, false).ShouldBe((true, false));
        f2.Invoke<sbyte, sbyte>(1, 2).ShouldBe(((sbyte)1, (sbyte)2));
        f3.Invoke<byte, byte>(1, 2).ShouldBe(((byte)1, (byte)2));
        f4.Invoke<short, short>(1, 2).ShouldBe(((short)1, (short)2));
        f5.Invoke<ushort, ushort>(1, 2).ShouldBe(((ushort)1, (ushort)2));
        f6.Invoke<int, int>(1, 2).ShouldBe((1, 2));
        f7.Invoke<uint, uint>(1, 2).ShouldBe(((uint)1, (uint)2));
        f8.Invoke<long, long>(1, 2).ShouldBe((1L, 2L));
        f9.Invoke<ulong, ulong>(1, 2).ShouldBe(((ulong)1, (ulong)2));
        f10.Invoke<Int128, Int128>(1, 2).ShouldBe((1, 2));
        f11.Invoke<UInt128, UInt128>(1, 2).ShouldBe(((UInt128)1, (UInt128)2));
        f12.Invoke<string, string>("1", "2").ShouldBe(("1", "2"));
        f13.Invoke<Half, Half>((Half)1, (Half)2).ShouldBe(((Half)1, (Half)2));
        f14.Invoke<float, float>(1f, 2f).ShouldBe((1f, 2f));
        f15.Invoke<double, double>(1d, 2d).ShouldBe((1d, 2d));
        f16.Invoke<decimal, decimal>(1, 2).ShouldBe((1m, 2m));
    }

    [Fact]
    public void Func_TwoArgsNullable_Returns()
    {
        using LuauFunction f1 = _state.CreateFunction((bool? x1, bool? x2) => (x1, x2));
        using LuauFunction f2 = _state.CreateFunction((sbyte? x1, sbyte? x2) => (x1, x2));
        using LuauFunction f3 = _state.CreateFunction((byte? x1, byte? x2) => (x1, x2));
        using LuauFunction f4 = _state.CreateFunction((short? x1, short? x2) => (x1, x2));
        using LuauFunction f5 = _state.CreateFunction((ushort? x1, ushort? x2) => (x1, x2));
        using LuauFunction f6 = _state.CreateFunction((int? x1, int? x2) => (x1, x2));
        using LuauFunction f7 = _state.CreateFunction((uint? x1, uint? x2) => (x1, x2));
        using LuauFunction f8 = _state.CreateFunction((long? x1, long? x2) => (x1, x2));
        using LuauFunction f9 = _state.CreateFunction((ulong? x1, ulong? x2) => (x1, x2));
        using LuauFunction f10 = _state.CreateFunction((Int128? x1, Int128? x2) => (x1, x2));
        using LuauFunction f11 = _state.CreateFunction((UInt128? x1, UInt128? x2) => (x1, x2));
        using LuauFunction f12 = _state.CreateFunction((string? x1, string? x2) => (x1, x2));
        using LuauFunction f13 = _state.CreateFunction((Half? x1, Half? x2) => (x1, x2));
        using LuauFunction f14 = _state.CreateFunction((float? x1, float? x2) => (x1, x2));
        using LuauFunction f15 = _state.CreateFunction((double? x1, double? x2) => (x1, x2));
        using LuauFunction f16 = _state.CreateFunction((decimal? x1, decimal? x2) => (x1, x2));

        f1.Invoke<bool?, bool?>((bool?)null, (bool?)null).ShouldBe((null, null));
        f2.Invoke<sbyte?, sbyte?>((sbyte?)null, (sbyte?)null).ShouldBe((null, null));
        f3.Invoke<byte?, byte?>((byte?)null, (byte?)null).ShouldBe((null, null));
        f4.Invoke<short?, short?>((short?)null, (short?)null).ShouldBe((null, null));
        f5.Invoke<ushort?, ushort?>((ushort?)null, (ushort?)null).ShouldBe((null, null));
        f6.Invoke<int?, int?>((int?)null, (int?)null).ShouldBe((null, null));
        f7.Invoke<uint?, uint?>((uint?)null, (uint?)null).ShouldBe((null, null));
        f8.Invoke<long?, long?>((long?)null, (long?)null).ShouldBe((null, null));
        f9.Invoke<ulong?, ulong?>((ulong?)null, (ulong?)null).ShouldBe((null, null));
        f10.Invoke<Int128?, Int128?>((long?)null, (long?)null).ShouldBe((null, null));
        f11.Invoke<UInt128?, UInt128?>((long?)null, (long?)null).ShouldBe((null, null));
        f12.Invoke<string?, string?>((string?)null, (string?)null).ShouldBe((null, null));
        f13.Invoke<Half?, Half?>((Half?)null, (Half?)null).ShouldBe((null, null));
        f14.Invoke<float?, float?>((float?)null, (float?)null).ShouldBe((null, null));
        f15.Invoke<double?, double?>((double?)null, (double?)null).ShouldBe((null, null));
        f16.Invoke<decimal?, decimal?>((double?)null, (double?)null).ShouldBe((null, null));
    }

    [Fact]
    public void Func_ThreeArgs_Returns()
    {
        using LuauFunction f1 = _state.CreateFunction((bool x1, bool x2, bool x3) => (x1, x2, x3));
        using LuauFunction f2 = _state.CreateFunction((sbyte x1, sbyte x2, sbyte x3) => (x1, x2, x3));
        using LuauFunction f3 = _state.CreateFunction((byte x1, byte x2, byte x3) => (x1, x2, x3));
        using LuauFunction f4 = _state.CreateFunction((short x1, short x2, short x3) => (x1, x2, x3));
        using LuauFunction f5 = _state.CreateFunction((ushort x1, ushort x2, ushort x3) => (x1, x2, x3));
        using LuauFunction f6 = _state.CreateFunction((int x1, int x2, int x3) => (x1, x2, x3));
        using LuauFunction f7 = _state.CreateFunction((uint x1, uint x2, uint x3) => (x1, x2, x3));
        using LuauFunction f8 = _state.CreateFunction((long x1, long x2, long x3) => (x1, x2, x3));
        using LuauFunction f9 = _state.CreateFunction((ulong x1, ulong x2, ulong x3) => (x1, x2, x3));
        using LuauFunction f10 = _state.CreateFunction((Int128 x1, Int128 x2, Int128 x3) => (x1, x2, x3));
        using LuauFunction f11 = _state.CreateFunction((UInt128 x1, UInt128 x2, UInt128 x3) => (x1, x2, x3));
        using LuauFunction f12 = _state.CreateFunction((string x1, string x2, string x3) => (x1, x2, x3));
        using LuauFunction f13 = _state.CreateFunction((Half x1, Half x2, Half x3) => (x1, x2, x3));
        using LuauFunction f14 = _state.CreateFunction((float x1, float x2, float x3) => (x1, x2, x3));
        using LuauFunction f15 = _state.CreateFunction((double x1, double x2, double x3) => (x1, x2, x3));
        using LuauFunction f16 = _state.CreateFunction((decimal x1, decimal x2, decimal x3) => (x1, x2, x3));

        f1.Invoke<bool, bool, bool>(true, false, true).ShouldBe((true, false, true));
        f2.Invoke<sbyte, sbyte, sbyte>(1, 2, 3).ShouldBe(((sbyte)1, (sbyte)2, (sbyte)3));
        f3.Invoke<byte, byte, byte>(1, 2, 3).ShouldBe(((byte)1, (byte)2, (byte)3));
        f4.Invoke<short, short, short>(1, 2, 3).ShouldBe(((short)1, (short)2, (short)3));
        f5.Invoke<ushort, ushort, ushort>(1, 2, 3).ShouldBe(((ushort)1, (ushort)2, (ushort)3));
        f6.Invoke<int, int, int>(1, 2, 3).ShouldBe((1, 2, 3));
        f7.Invoke<uint, uint, uint>(1, 2, 3).ShouldBe(((uint)1, (uint)2, (uint)3));
        f8.Invoke<long, long, long>(1, 2, 3).ShouldBe((1L, 2L, 3L));
        f9.Invoke<ulong, ulong, ulong>(1, 2, 3).ShouldBe(((ulong)1, (ulong)2, (ulong)3));
        f10.Invoke<Int128, Int128, Int128>(1, 2, 3).ShouldBe((1, 2, 3));
        f11.Invoke<UInt128, UInt128, UInt128>(1, 2, 3).ShouldBe(((UInt128)1, (UInt128)2, (UInt128)3));
        f12.Invoke<string, string, string>("1", "2", "3").ShouldBe(("1", "2", "3"));
        f13.Invoke<Half, Half, Half>((Half)1, (Half)2, (Half)3).ShouldBe(((Half)1, (Half)2, (Half)3));
        f14.Invoke<float, float, float>(1f, 2f, 3f).ShouldBe((1f, 2f, 3f));
        f15.Invoke<double, double, double>(1d, 2d, 3d).ShouldBe((1d, 2d, 3d));
        f16.Invoke<decimal, decimal, decimal>(1, 2, 3).ShouldBe((1m, 2m, 3m));
    }

    [Fact]
    public void Func_ThreeArgsNullable_Returns()
    {
        using LuauFunction f1 = _state.CreateFunction((bool? x1, bool? x2, bool? x3) => (x1, x2, x3));
        using LuauFunction f2 = _state.CreateFunction((sbyte? x1, sbyte? x2, sbyte? x3) => (x1, x2, x3));
        using LuauFunction f3 = _state.CreateFunction((byte? x1, byte? x2, byte? x3) => (x1, x2, x3));
        using LuauFunction f4 = _state.CreateFunction((short? x1, short? x2, short? x3) => (x1, x2, x3));
        using LuauFunction f5 = _state.CreateFunction((ushort? x1, ushort? x2, ushort? x3) => (x1, x2, x3));
        using LuauFunction f6 = _state.CreateFunction((int? x1, int? x2, int? x3) => (x1, x2, x3));
        using LuauFunction f7 = _state.CreateFunction((uint? x1, uint? x2, uint? x3) => (x1, x2, x3));
        using LuauFunction f8 = _state.CreateFunction((long? x1, long? x2, long? x3) => (x1, x2, x3));
        using LuauFunction f9 = _state.CreateFunction((ulong? x1, ulong? x2, ulong? x3) => (x1, x2, x3));
        using LuauFunction f10 = _state.CreateFunction((Int128? x1, Int128? x2, Int128? x3) => (x1, x2, x3));
        using LuauFunction f11 = _state.CreateFunction((UInt128? x1, UInt128? x2, UInt128? x3) => (x1, x2, x3));
        using LuauFunction f12 = _state.CreateFunction((string? x1, string? x2, string? x3) => (x1, x2, x3));
        using LuauFunction f13 = _state.CreateFunction((Half? x1, Half? x2, Half? x3) => (x1, x2, x3));
        using LuauFunction f14 = _state.CreateFunction((float? x1, float? x2, float? x3) => (x1, x2, x3));
        using LuauFunction f15 = _state.CreateFunction((double? x1, double? x2, double? x3) => (x1, x2, x3));
        using LuauFunction f16 = _state.CreateFunction((decimal? x1, decimal? x2, decimal? x3) => (x1, x2, x3));

        f1.Invoke<bool?, bool?, bool?>((bool?)null, (bool?)null, (bool?)null).ShouldBe((null, null, null));
        f2.Invoke<sbyte?, sbyte?, sbyte?>((sbyte?)null, (sbyte?)null, (sbyte?)null).ShouldBe((null, null, null));
        f3.Invoke<byte?, byte?, byte?>((byte?)null, (byte?)null, (byte?)null).ShouldBe((null, null, null));
        f4.Invoke<short?, short?, short?>((short?)null, (short?)null, (short?)null).ShouldBe((null, null, null));
        f5.Invoke<ushort?, ushort?, ushort?>((ushort?)null, (ushort?)null, (ushort?)null).ShouldBe((null, null, null));
        f6.Invoke<int?, int?, int?>((int?)null, (int?)null, (int?)null).ShouldBe((null, null, null));
        f7.Invoke<uint?, uint?, uint?>((uint?)null, (uint?)null, (uint?)null).ShouldBe((null, null, null));
        f8.Invoke<long?, long?, long?>((long?)null, (long?)null, (long?)null).ShouldBe((null, null, null));
        f9.Invoke<ulong?, ulong?, ulong?>((ulong?)null, (ulong?)null, (ulong?)null).ShouldBe((null, null, null));
        f10.Invoke<Int128?, Int128?, Int128?>((long?)null, (long?)null, (long?)null).ShouldBe((null, null, null));
        f11.Invoke<UInt128?, UInt128?, UInt128?>((long?)null, (long?)null, (long?)null).ShouldBe((null, null, null));
        f12.Invoke<string?, string?, string?>((string?)null, (string?)null, (string?)null).ShouldBe((null, null, null));
        f13.Invoke<Half?, Half?, Half?>((Half?)null, (Half?)null, (Half?)null).ShouldBe((null, null, null));
        f14.Invoke<float?, float?, float?>((float?)null, (float?)null, (float?)null).ShouldBe((null, null, null));
        f15.Invoke<double?, double?, double?>((double?)null, (double?)null, (double?)null).ShouldBe((null, null, null));
        f16.Invoke<decimal?, decimal?, decimal?>((double?)null, (double?)null, (double?)null)
            .ShouldBe((null, null, null));
    }

    [Fact]
    public void Func_FourArgs_Returns()
    {
        using LuauFunction f1 = _state.CreateFunction((bool x1, bool x2, bool x3, bool x4) => (x1, x2, x3, x4));
        using LuauFunction f2 = _state.CreateFunction((sbyte x1, sbyte x2, sbyte x3, sbyte x4) => (x1, x2, x3, x4));
        using LuauFunction f3 = _state.CreateFunction((byte x1, byte x2, byte x3, byte x4) => (x1, x2, x3, x4));
        using LuauFunction f4 = _state.CreateFunction((short x1, short x2, short x3, short x4) => (x1, x2, x3, x4));
        using LuauFunction f5 = _state.CreateFunction((ushort x1, ushort x2, ushort x3, ushort x4) => (x1, x2, x3, x4));
        using LuauFunction f6 = _state.CreateFunction((int x1, int x2, int x3, int x4) => (x1, x2, x3, x4));
        using LuauFunction f7 = _state.CreateFunction((uint x1, uint x2, uint x3, uint x4) => (x1, x2, x3, x4));
        using LuauFunction f8 = _state.CreateFunction((long x1, long x2, long x3, long x4) => (x1, x2, x3, x4));
        using LuauFunction f9 = _state.CreateFunction((ulong x1, ulong x2, ulong x3, ulong x4) => (x1, x2, x3, x4));
        using LuauFunction f10 = _state.CreateFunction(
            (Int128 x1, Int128 x2, Int128 x3, Int128 x4) => (x1, x2, x3, x4)
        );
        using LuauFunction f11 = _state.CreateFunction(
            (UInt128 x1, UInt128 x2, UInt128 x3, UInt128 x4) => (x1, x2, x3, x4)
        );
        using LuauFunction f12 = _state.CreateFunction(
            (string x1, string x2, string x3, string x4) => (x1, x2, x3, x4)
        );
        using LuauFunction f13 = _state.CreateFunction((Half x1, Half x2, Half x3, Half x4) => (x1, x2, x3, x4));
        using LuauFunction f14 = _state.CreateFunction((float x1, float x2, float x3, float x4) => (x1, x2, x3, x4));
        using LuauFunction f15 = _state.CreateFunction(
            (double x1, double x2, double x3, double x4) => (x1, x2, x3, x4)
        );
        using LuauFunction f16 = _state.CreateFunction(
            (decimal x1, decimal x2, decimal x3, decimal x4) => (x1, x2, x3, x4)
        );

        f1.Invoke<bool, bool, bool, bool>(true, false, true, false).ShouldBe((true, false, true, false));
        f2.Invoke<sbyte, sbyte, sbyte, sbyte>(1, 2, 3, 4).ShouldBe(((sbyte)1, (sbyte)2, (sbyte)3, (sbyte)4));
        f3.Invoke<byte, byte, byte, byte>(1, 2, 3, 4).ShouldBe(((byte)1, (byte)2, (byte)3, (byte)4));
        f4.Invoke<short, short, short, short>(1, 2, 3, 4).ShouldBe(((short)1, (short)2, (short)3, (short)4));
        f5.Invoke<ushort, ushort, ushort, ushort>(1, 2, 3, 4).ShouldBe(((ushort)1, (ushort)2, (ushort)3, (ushort)4));
        f6.Invoke<int, int, int, int>(1, 2, 3, 4).ShouldBe((1, 2, 3, 4));
        f7.Invoke<uint, uint, uint, uint>(1, 2, 3, 4).ShouldBe(((uint)1, (uint)2, (uint)3, (uint)4));
        f8.Invoke<long, long, long, long>(1, 2, 3, 4).ShouldBe((1L, 2L, 3L, 4L));
        f9.Invoke<ulong, ulong, ulong, ulong>(1, 2, 3, 4).ShouldBe(((ulong)1, (ulong)2, (ulong)3, (ulong)4));
        f10.Invoke<Int128, Int128, Int128, Int128>(1, 2, 3, 4).ShouldBe((1, 2, 3, 4));
        f11.Invoke<UInt128, UInt128, UInt128, UInt128>(1, 2, 3, 4)
            .ShouldBe(((UInt128)1, (UInt128)2, (UInt128)3, (UInt128)4));
        f12.Invoke<string, string, string, string>("1", "2", "3", "4").ShouldBe(("1", "2", "3", "4"));
        f13.Invoke<Half, Half, Half, Half>((Half)1, (Half)2, (Half)3, (Half)4)
            .ShouldBe(((Half)1, (Half)2, (Half)3, (Half)4));
        f14.Invoke<float, float, float, float>(1f, 2f, 3f, 4f).ShouldBe((1f, 2f, 3f, 4f));
        f15.Invoke<double, double, double, double>(1d, 2d, 3d, 4d).ShouldBe((1d, 2d, 3d, 4d));
        f16.Invoke<decimal, decimal, decimal, decimal>(1, 2, 3, 4).ShouldBe((1m, 2m, 3m, 4m));
    }

    [Fact]
    public void Func_FourArgsNullable_Returns()
    {
        using LuauFunction f1 = _state.CreateFunction((bool? x1, bool? x2, bool? x3, bool? x4) => (x1, x2, x3, x4));
        using LuauFunction f2 = _state.CreateFunction((sbyte? x1, sbyte? x2, sbyte? x3, sbyte? x4) => (x1, x2, x3, x4));
        using LuauFunction f3 = _state.CreateFunction((byte? x1, byte? x2, byte? x3, byte? x4) => (x1, x2, x3, x4));
        using LuauFunction f4 = _state.CreateFunction((short? x1, short? x2, short? x3, short? x4) => (x1, x2, x3, x4));
        using LuauFunction f5 = _state.CreateFunction(
            (ushort? x1, ushort? x2, ushort? x3, ushort? x4) => (x1, x2, x3, x4)
        );
        using LuauFunction f6 = _state.CreateFunction((int? x1, int? x2, int? x3, int? x4) => (x1, x2, x3, x4));
        using LuauFunction f7 = _state.CreateFunction((uint? x1, uint? x2, uint? x3, uint? x4) => (x1, x2, x3, x4));
        using LuauFunction f8 = _state.CreateFunction((long? x1, long? x2, long? x3, long? x4) => (x1, x2, x3, x4));
        using LuauFunction f9 = _state.CreateFunction((ulong? x1, ulong? x2, ulong? x3, ulong? x4) => (x1, x2, x3, x4));
        using LuauFunction f10 = _state.CreateFunction(
            (Int128? x1, Int128? x2, Int128? x3, Int128? x4) => (x1, x2, x3, x4)
        );
        using LuauFunction f11 = _state.CreateFunction(
            (UInt128? x1, UInt128? x2, UInt128? x3, UInt128? x4) => (x1, x2, x3, x4)
        );
        using LuauFunction f12 = _state.CreateFunction(
            (string? x1, string? x2, string? x3, string? x4) => (x1, x2, x3, x4)
        );
        using LuauFunction f13 = _state.CreateFunction((Half? x1, Half? x2, Half? x3, Half? x4) => (x1, x2, x3, x4));
        using LuauFunction f14 = _state.CreateFunction(
            (float? x1, float? x2, float? x3, float? x4) => (x1, x2, x3, x4)
        );
        using LuauFunction f15 = _state.CreateFunction(
            (double? x1, double? x2, double? x3, double? x4) => (x1, x2, x3, x4)
        );
        using LuauFunction f16 = _state.CreateFunction(
            (decimal? x1, decimal? x2, decimal? x3, decimal? x4) => (x1, x2, x3, x4)
        );

        f1.Invoke<bool?, bool?, bool?, bool?>((bool?)null, (bool?)null, (bool?)null, (bool?)null)
            .ShouldBe((null, null, null, null));
        f2.Invoke<sbyte?, sbyte?, sbyte?, sbyte?>((sbyte?)null, (sbyte?)null, (sbyte?)null, (sbyte?)null)
            .ShouldBe((null, null, null, null));
        f3.Invoke<byte?, byte?, byte?, byte?>((byte?)null, (byte?)null, (byte?)null, (byte?)null)
            .ShouldBe((null, null, null, null));
        f4.Invoke<short?, short?, short?, short?>((short?)null, (short?)null, (short?)null, (short?)null)
            .ShouldBe((null, null, null, null));
        f5.Invoke<ushort?, ushort?, ushort?, ushort?>((ushort?)null, (ushort?)null, (ushort?)null, (ushort?)null)
            .ShouldBe((null, null, null, null));
        f6.Invoke<int?, int?, int?, int?>((int?)null, (int?)null, (int?)null, (int?)null)
            .ShouldBe((null, null, null, null));
        f7.Invoke<uint?, uint?, uint?, uint?>((uint?)null, (uint?)null, (uint?)null, (uint?)null)
            .ShouldBe((null, null, null, null));
        f8.Invoke<long?, long?, long?, long?>((long?)null, (long?)null, (long?)null, (long?)null)
            .ShouldBe((null, null, null, null));
        f9.Invoke<ulong?, ulong?, ulong?, ulong?>((ulong?)null, (ulong?)null, (ulong?)null, (ulong?)null)
            .ShouldBe((null, null, null, null));
        f10.Invoke<Int128?, Int128?, Int128?, Int128?>((long?)null, (long?)null, (long?)null, (long?)null)
            .ShouldBe((null, null, null, null));
        f11.Invoke<UInt128?, UInt128?, UInt128?, UInt128?>((long?)null, (long?)null, (long?)null, (long?)null)
            .ShouldBe((null, null, null, null));
        f12.Invoke<string?, string?, string?, string?>((string?)null, (string?)null, (string?)null, (string?)null)
            .ShouldBe((null, null, null, null));
        f13.Invoke<Half?, Half?, Half?, Half?>((Half?)null, (Half?)null, (Half?)null, (Half?)null)
            .ShouldBe((null, null, null, null));
        f14.Invoke<float?, float?, float?, float?>((float?)null, (float?)null, (float?)null, (float?)null)
            .ShouldBe((null, null, null, null));
        f15.Invoke<double?, double?, double?, double?>((double?)null, (double?)null, (double?)null, (double?)null)
            .ShouldBe((null, null, null, null));
        f16.Invoke<decimal?, decimal?, decimal?, decimal?>((double?)null, (double?)null, (double?)null, (double?)null)
            .ShouldBe((null, null, null, null));
    }

    [Fact]
    public void Func_NoArgs_Returns_ULong()
    {
        const ulong expectedValue = 1;

        using LuauFunction func = _state.CreateFunction(() => expectedValue);
        _state.Globals.Set("f", func);
        _state.Load("result = f()").Execute();
        _state.Globals.TryGet("result", out ulong result).ShouldBeTrue();

        result.ShouldBe(expectedValue);
    }

    [Fact]
    public void Func_NoArgs_Returns_Nil_Bool()
    {
        bool? expectedValue = null;

        using LuauFunction func = _state.CreateFunction(() => expectedValue);
        _state.Globals.Set("f", func);
        _state.Load("result = f()").Execute();
        _state.Globals.TryGet("result", out bool? result).ShouldBeFalse();

        result.ShouldBe(expectedValue);
    }

    [Fact]
    public void Func_NoArgs_Returns_Int()
    {
        const int expectedValue = 1;

        using LuauFunction func = _state.CreateFunction(() => expectedValue);
        _state.Globals.Set("f", func);
        _state.Load("result = f()").Execute();
        _state.Globals.TryGet("result", out int result).ShouldBeTrue();

        result.ShouldBe(expectedValue);
    }

    [Fact]
    public void Func_BufferArg_ReturnsBuffer()
    {
        const string expectedValue = "010203";

        using LuauBuffer buffer = _state.CreateBuffer(Convert.FromHexString(expectedValue));
        using LuauFunction func = _state.CreateFunctionBuilder(static args =>
        {
            if (!args.TryReadLuauBuffer(1, out LuauBufferView b, out string? error))
                return LuauReturn.Error(error);

            return LuauReturn.Ok(b);
        });

        _state.Globals.Set("input", buffer);
        _state.Globals.Set("f", func);
        _state.Load("result = f(input)").Execute();
        _state.Globals.TryGet("result", out LuauBuffer bufferResult).ShouldBeTrue();

        using (bufferResult)
        {
            bufferResult.TryGet(out byte[]? bufferBytes);
            Convert.ToHexString(bufferBytes!).ShouldBe(expectedValue);
        }
    }

    [Fact]
    public void Func_StringArg_ReturnsBuffer()
    {
        byte[] expected = [0x01, 0x02, 0x03];
        using LuauFunction func = _state.CreateFunctionBuilder(static args =>
        {
            if (!args.TryReadUtf8String(1, out string? hex, out string? error))
                return LuauReturn.Error(error);

            return LuauReturn.Ok(Convert.FromHexString(hex));
        });

        _state.Globals.Set("input", Convert.ToHexString(expected));
        _state.Globals.Set("f", func);
        _state.Load("result = f(input)").Execute();
        LuauMarshal.TryGetBufferSpan(_state.Globals, "result", out ReadOnlySpan<byte> result).ShouldBeTrue();
        result.ToArray().ShouldBe<byte>(expected);
    }

    [Fact]
    public void Func_StringArg_ShouldReadLuauStringViewAndReturnSameValue()
    {
        const string expectedValue = "hello from luau";

        using LuauFunction func = _state.CreateFunctionBuilder(static args =>
        {
            if (!args.TryReadLuauString(1, out LuauStringView value, out string? error))
                return LuauReturn.Error(error);

            return LuauReturn.Ok(value);
        });
        _state.Globals.Set("input", expectedValue);
        _state.Globals.Set("f", func);

        _state.Load("result = f(input)").Execute();
        _state.Globals.GetUtf8String("result").ShouldBe(expectedValue);
    }

    [Fact]
    public void Func_UserdataArg_ShouldReadManagedUserdata()
    {
        var input = new ArgsUserdataA { Value = 42 };
        using LuauFunction func = _state.CreateFunctionBuilder(static args =>
        {
            if (!args.TryReadUserdata(1, out ArgsUserdataA? value, out string? error))
                return LuauReturn.Error(error);

            return LuauReturn.Ok(value.Value);
        });
        _state.Globals.Set("input", input);
        _state.Globals.Set("f", func);

        _state.Load("result = f(input)").Execute();
        _state.Globals.GetNumber("result").ShouldBe(42);
    }

    [Fact]
    public void CreateFunction_UserdataArg_ShouldReadManagedUserdata()
    {
        var input = new ArgsUserdataA { Value = 42 };
        using LuauFunction func = _state.CreateFunction((ArgsUserdataA value) => value.Value);
        _state.Globals.Set("input", input);
        _state.Globals.Set("f", func);

        int result = _state.Load("return f(input)").Execute<int>();
        result.ShouldBe(42);
    }

    [Fact]
    public void Func_UserdataArg_WrongType_ShouldReturnError()
    {
        using LuauFunction func = _state.CreateFunctionBuilder(static args =>
        {
            if (!args.TryReadUserdata<ArgsUserdataA>(1, out _, out string? error))
                return LuauReturn.Error(error);

            return LuauReturn.Ok();
        });
        _state.Globals.Set("input", new ArgsUserdataB());
        _state.Globals.Set("f", func);

        _state
            .Load(
                """
                ok, err = pcall(function()
                  f(input)
                end)
                """
            )
            .Execute();

        _state.Globals.GetBoolean("ok").ShouldBeFalse();
        _state.Globals.GetUtf8String("err").ShouldContain("must be userdata of type");
    }

    [Fact]
    public void CreateFunction_UserdataArg_WrongType_ShouldReturnError()
    {
        using LuauFunction func = _state.CreateFunction(static (ArgsUserdataA value) => value.Value);
        _state.Globals.Set("input", new ArgsUserdataB());
        _state.Globals.Set("f", func);

        _state
            .Load(
                """
                ok, err = pcall(function()
                  f(input)
                end)
                """
            )
            .Execute();

        _state.Globals.GetBoolean("ok").ShouldBeFalse();
        _state.Globals.GetUtf8String("err").ShouldContain("must be userdata of type");
    }

    [Fact]
    public void Func_UserdataArgOrNil_ShouldHandleNil()
    {
        using LuauFunction func = _state.CreateFunctionBuilder(static args =>
        {
            if (!args.TryReadUserdataOrNil(1, out ArgsUserdataA? value, out string? error))
                return LuauReturn.Error(error);

            return LuauReturn.Ok(value is null ? "nil" : "value");
        });
        _state.Globals.Set("f", func);

        _state.Load("fromNil = f(nil)").Execute();

        _state.Globals.Set("input", new ArgsUserdataA());
        _state.Load("fromUserdata = f(input)").Execute();

        _state.Globals.TryGet("fromNil", out string? fromNil).ShouldBeTrue();
        fromNil.ShouldBe("nil");
        _state.Globals.TryGet("fromUserdata", out string? fromUserdata).ShouldBeTrue();
        fromUserdata.ShouldBe("value");
    }

    [Fact]
    public void CreateFunction_UserdataArgOrNil_ShouldHandleNil()
    {
        using LuauFunction func = _state.CreateFunction((ArgsUserdataA? value) => value is null ? "nil" : "value");
        _state.Globals.Set("f", func);

        string fromNil = _state.Load("return f(nil)").Execute<string>();
        _state.Globals.Set("input", new ArgsUserdataA());
        string fromUserdata = _state.Load("return f(input)").Execute<string>();

        fromNil.ShouldBe("nil");
        fromUserdata.ShouldBe("value");
    }

    [Fact]
    public void CreateFunction_ReturningManagedUserdata_ShouldNotRequireImplicitConversion()
    {
        var input = new GeneratedReturnUserdata();
        using LuauFunction func = _state.CreateFunction(() => input);
        _state.Globals.Set("input", IntoLuau.FromUserdata(input));
        _state.Globals.Set("f", func);

        _state.Load("result = f(); isSame = result == input").Execute();

        _state.Globals.TryGet("isSame", out bool isSame).ShouldBeTrue();
        isSame.ShouldBeTrue();
        _state.Globals.TryGetUserdata("result", out GeneratedReturnUserdata? result).ShouldBeTrue();
        ReferenceEquals(input, result).ShouldBeTrue();
    }

    [Fact]
    public void CreateFunction_ReturningNullableManagedUserdata_FromLambda_ShouldReturnNil()
    {
        using LuauFunction func = _state.CreateFunction(() => (GeneratedReturnUserdata?)null);
        _state.Globals.Set("f", func);

        _state.Load("isNil = f() == nil").Execute();

        _state.Globals.GetBoolean("isNil").ShouldBeTrue();
    }

    [Fact]
    public void CreateFunction_ReturningNullableManagedUserdata_ShouldReturnNil()
    {
        Func<GeneratedReturnUserdata?> callback = static () => null;
        using LuauFunction func = _state.CreateFunction(callback);
        _state.Globals.Set("f", func);

        _state.Load("isNil = f() == nil").Execute();

        _state.Globals.TryGet("isNil", out bool isNil).ShouldBeTrue();
        isNil.ShouldBeTrue();
    }

    [Fact]
    public void CreateFunction_ReturningNullableManagedUserdata_FromMultipleBranches_ShouldReturnNilOrUserdata()
    {
        bool returnNil = true;
        var instance = new GeneratedReturnUserdata();
        using LuauFunction func = _state.CreateFunction(() =>
        {
            if (returnNil)
                return (GeneratedReturnUserdata?)null;
            return instance;
        });
        _state.Globals.Set("f", func);

        _state.Load("isNil = f() == nil").Execute();
        _state.Globals.GetBoolean("isNil").ShouldBeTrue();

        returnNil = false;
        _state.Load("result = f()").Execute();
        GeneratedReturnUserdata result = _state.Globals.GetUserdata<GeneratedReturnUserdata>("result");
        ReferenceEquals(instance, result).ShouldBeTrue();
    }

    [Fact]
    public void CreateFunction_TupleReturn_WithNullableManagedUserdata_FromMultipleBranches_ShouldReturnNilOrUserdata()
    {
        bool returnNil = true;
        var instance = new GeneratedReturnUserdata();
        using LuauFunction func = _state.CreateFunction(() =>
        {
            if (returnNil)
                return ((GeneratedReturnUserdata?)null, 7);
            return (instance, 7);
        });
        _state.Globals.Set("f", func);

        _state.Load("resultUserdata, resultNumber = f(); isNil = resultUserdata == nil").Execute();
        _state.Globals.GetBoolean("isNil").ShouldBeTrue();
        _state.Globals.GetNumber("resultNumber").ShouldBe(7);

        returnNil = false;
        _state.Load("resultUserdata, resultNumber = f()").Execute();
        GeneratedReturnUserdata result = _state.Globals.GetUserdata<GeneratedReturnUserdata>("resultUserdata");
        ReferenceEquals(instance, result).ShouldBeTrue();
        _state.Globals.GetNumber("resultNumber").ShouldBe(7);
    }

    [Fact]
    public void CreateFunction_ReturningWrongNullabilityShouldThrow()
    {
        using LuauFunction func = _state.CreateFunction((Func<GeneratedReturnUserdata>)Callback);
        _state.Globals.Set("f", func);

        Should
            .Throw<LuaException>(() => _state.Load("return f()").Execute())
            .Message.ShouldContain("Value cannot be null. (Parameter 'userdata'");
        return;

        static GeneratedReturnUserdata Callback() => null!;
    }

    [Fact]
    public void CreateFunction_TupleReturn_WithManagedUserdata_ShouldWork()
    {
        var input = new GeneratedReturnUserdata();
        using LuauFunction func = _state.CreateFunction(() => (input, 7));
        _state.Globals.Set("input", IntoLuau.FromUserdata(input));
        _state.Globals.Set("f", func);

        _state.Load("resultUserdata, resultNumber = f(); isSame = resultUserdata == input").Execute();

        _state.Globals.GetBoolean("isSame").ShouldBeTrue();
        _state.Globals.GetNumber("resultNumber").ShouldBe(7);
        GeneratedReturnUserdata resultUserdata = _state.Globals.GetUserdata<GeneratedReturnUserdata>("resultUserdata");
        ReferenceEquals(input, resultUserdata).ShouldBeTrue();
    }

    [Fact]
    public void CreateFunction_TupleReturn_WithNullableManagedUserdata_ShouldReturnNilAndOtherValues()
    {
        using LuauFunction func = _state.CreateFunction(() => ((GeneratedReturnUserdata?)null, 7));
        _state.Globals.Set("f", func);

        _state.Load("resultUserdata, resultNumber = f(); isNil = resultUserdata == nil").Execute();

        _state.Globals.GetBoolean("isNil").ShouldBeTrue();
        _state.Globals.GetNumber("resultNumber").ShouldBe(7);
    }

    [Fact]
    public void Func_UserdataArg_ShouldReadLuauUserdataReference()
    {
        using LuauFunction func = _state.CreateFunctionBuilder(static args =>
        {
            if (!args.TryReadLuauUserdata(1, out LuauUserdataView value, out string? error))
                return LuauReturn.Error(error);

            return LuauReturn.Ok(value);
        });
        _state.Globals.Set("input", new ArgsUserdataA());
        _state.Globals.Set("f", func);

        _state.Load("isSame = f(input) == input").Execute();
        _state.Globals.TryGet("isSame", out bool isSame).ShouldBeTrue();
        isSame.ShouldBeTrue();
    }

    [Fact]
    public void LuauFunction_Invoke_ReturningTable_ShouldReturnUsableTable()
    {
        _state
            .Load(
                """
                function mk_table()
                  return { value = 7 }
                end
                """
            )
            .Execute();

        _state.Globals.TryGet("mk_table", out LuauFunction mkTable).ShouldBeTrue();
        using (mkTable)
        {
            using LuauTable table = mkTable.Invoke<LuauTable>();
            table.GetNumber("value").ShouldBe(7);
        }
    }

    [Fact]
    public void LuauFunction_Invoke_ReturningTable_ShouldKeepReferenceTrackedUntilCallerDisposes()
    {
        _state
            .Load(
                """
                function mk_table()
                  return { value = 11 }
                end
                """
            )
            .Execute();

        _state.Globals.TryGet("mk_table", out LuauFunction mkTable).ShouldBeTrue();
        using (mkTable)
        {
            ulong baselineActiveReferences = _state.MemoryStatistics.ActiveRegistryReferences;

            using (LuauTable table = mkTable.Invoke<LuauTable>())
            {
                _state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(baselineActiveReferences + 1);
                table.GetNumber("value").ShouldBe(11);
            }

            _state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(baselineActiveReferences);
        }
    }

    [Fact]
    public void LuauFunctionView_Invoke_ReturningTable_ShouldWorkInsideManagedCallback()
    {
        _state
            .Load(
                """
                function make_table()
                  return { value = 99 }
                end
                """
            )
            .Execute();

        using LuauFunction callAndRead = _state.CreateFunctionBuilder(static args =>
        {
            if (!args.TryReadLuauFunction(1, out LuauFunctionView function, out string? error))
                return LuauReturn.Error(error);

            using LuauTable table = function.Invoke<LuauTable>();
            if (!table.TryGet("value", out int value))
                return LuauReturn.Error("Expected returned table to contain number key 'value'.");

            return LuauReturn.Ok(value);
        });

        _state.Globals.Set("call_and_read", callAndRead);
        _state.Load("result = call_and_read(make_table)").Execute();

        _state.Globals.GetNumber("result").ShouldBe(99);
    }

    [Fact]
    public void LuauFunctionView_Invoke_FromCoroutine_ShouldWorkInsideManagedCallback()
    {
        _state
            .Load(
                """
                function make_value()
                  return 42
                end
                """
            )
            .Execute();

        using LuauFunction callAndRead = _state.CreateFunctionBuilder(static args =>
        {
            if (!args.TryReadLuauFunction(1, out LuauFunctionView function, out string? error))
                return LuauReturn.Error(error);

            return LuauReturn.Ok(function.Invoke<int>());
        });

        _state.Globals.Set("call_and_read", callAndRead);

        int result = _state
            .Load(
                """
                local co = coroutine.create(function()
                  return call_and_read(make_value)
                end)
                local ok, value = coroutine.resume(co)
                if not ok then error(value) end
                return value
                """
            )
            .Execute<int>();

        result.ShouldBe(42);
    }

    [Fact]
    public void LuauFunctionView_Invoke_TupleReturn_ShouldWorkInsideManagedCallback()
    {
        _state
            .Load(
                """
                function make_pair()
                  return 7, "value"
                end
                """
            )
            .Execute();

        using LuauFunction callAndRead = _state.CreateFunctionBuilder(static args =>
        {
            if (!args.TryReadLuauFunction(1, out LuauFunctionView function, out string? error))
                return LuauReturn.Error(error);

            (int number, string? text) = function.Invoke<int, string?>();
            return LuauReturn.Ok($"{number}:{text}");
        });

        _state.Globals.Set("call_and_read", callAndRead);
        _state.Load("result = call_and_read(make_pair)").Execute();

        _state.Globals.TryGet("result", out string? result).ShouldBeTrue();
        result.ShouldBe("7:value");
    }

    [Fact]
    public void LuauFunctionView_Invoke_Void_ShouldWorkInsideManagedCallback()
    {
        _state
            .Load(
                """
                function make_values()
                  return 1, 2
                end
                """
            )
            .Execute();

        using LuauFunction callAndRead = _state.CreateFunctionBuilder(static args =>
        {
            if (!args.TryReadLuauFunction(1, out LuauFunctionView function, out string? error))
                return LuauReturn.Error(error);

            function.Invoke();
            return LuauReturn.Ok("done");
        });

        _state.Globals.Set("call_and_read", callAndRead);
        _state.Load("result = call_and_read(make_values)").Execute();

        _state.Globals.TryGet("result", out string? result).ShouldBeTrue();
        result.ShouldBe("done");
    }

    [Fact]
    public void LuauFunctionView_Invoke_ThreeReturn_ShouldWorkInsideManagedCallback()
    {
        _state
            .Load(
                """
                function make_values()
                  return 7, "value", true
                end
                """
            )
            .Execute();

        using LuauFunction callAndRead = _state.CreateFunctionBuilder(static args =>
        {
            if (!args.TryReadLuauFunction(1, out LuauFunctionView function, out string? error))
                return LuauReturn.Error(error);

            (int number, string? text, bool flag) = function.Invoke<int, string?, bool>();
            return LuauReturn.Ok($"{number}:{text}:{flag}");
        });

        _state.Globals.Set("call_and_read", callAndRead);
        _state.Load("result = call_and_read(make_values)").Execute();

        _state.Globals.TryGet("result", out string? result).ShouldBeTrue();
        result.ShouldBe("7:value:True");
    }

    [Fact]
    public void LuauFunctionView_Invoke_FourReturn_ShouldWorkInsideManagedCallback()
    {
        _state
            .Load(
                """
                function make_values()
                  return 7, "value", true, 15
                end
                """
            )
            .Execute();

        using LuauFunction callAndRead = _state.CreateFunctionBuilder(static args =>
        {
            if (!args.TryReadLuauFunction(1, out LuauFunctionView function, out string? error))
                return LuauReturn.Error(error);

            (int number, string? text, bool flag, int value) = function.Invoke<int, string?, bool, int>();
            return LuauReturn.Ok($"{number}:{text}:{flag}:{value}");
        });

        _state.Globals.Set("call_and_read", callAndRead);
        _state.Load("result = call_and_read(make_values)").Execute();

        _state.Globals.TryGet("result", out string? result).ShouldBeTrue();
        result.ShouldBe("7:value:True:15");
    }

    [Fact]
    public void LuauFunctionView_InvokeMulti_ShouldWorkInsideManagedCallback()
    {
        _state
            .Load(
                """
                function make_values()
                  return 7, "value", true
                end
                """
            )
            .Execute();

        using LuauFunction callAndRead = _state.CreateFunctionBuilder(static args =>
        {
            if (!args.TryReadLuauFunction(1, out LuauFunctionView function, out string? error))
                return LuauReturn.Error(error);

            LuauValue[] values = function.InvokeMulti();
            if (values.Length != 3)
                return LuauReturn.Error($"Expected 3 values, got {values.Length}.");

            using LuauValue value1 = values[0];
            using LuauValue value2 = values[1];
            using LuauValue value3 = values[2];

            if (!value1.TryGet(out int number, acceptNil: false))
                return LuauReturn.Error("Expected first return value to be an int.");
            if (!value2.TryGet(out string? text, acceptNil: false))
                return LuauReturn.Error("Expected second return value to be a string.");
            if (!value3.TryGet(out bool flag, acceptNil: false))
                return LuauReturn.Error("Expected third return value to be a bool.");

            return LuauReturn.Ok($"{number}:{text}:{flag}");
        });

        _state.Globals.Set("call_and_read", callAndRead);
        _state.Load("result = call_and_read(make_values)").Execute();

        _state.Globals.TryGet("result", out string? result).ShouldBeTrue();
        result.ShouldBe("7:value:True");
    }

    [Fact]
    public void Views_ToOwned_ShouldPromoteBorrowedReferences()
    {
        using LuauTable inputTable = _state.CreateTable();
        inputTable.Set("value", 7);

        using LuauFunction inputFunction = _state.CreateFunction((int value) => value + 1);
        byte[] inputBuffer = [0x01, 0x02, 0x03];
        var inputUserdata = new Fixtures.ValueUserdata { Value = 42 };

        LuauTable ownedTable = default;
        LuauString ownedString = default;
        LuauFunction ownedFunction = default;
        LuauBuffer ownedBuffer = default;
        LuauUserdata ownedUserdata = default;

        using LuauFunction capture = _state.CreateFunction(
            (
                LuauTableView table,
                LuauStringView text,
                LuauFunctionView function,
                LuauBufferView buffer,
                LuauUserdataView userdata
            ) =>
            {
                ownedTable = table.ToOwned();
                ownedString = text.ToOwned();
                ownedFunction = function.ToOwned();
                ownedBuffer = buffer.ToOwned();
                ownedUserdata = userdata.ToOwned();
            }
        );

        _state.Globals.Set("capture", capture);
        _state.Globals.Set("inputTable", inputTable);
        _state.Globals.Set("inputFunction", inputFunction);
        _state.Globals.Set("inputBuffer", inputBuffer);
        _state.Globals.Set("inputUserdata", inputUserdata);
        _state.Load("capture(inputTable, 'hello', inputFunction, inputBuffer, inputUserdata)").Execute();

        using (ownedTable)
            ownedTable.GetNumber("value").ShouldBe(7);

        using (ownedString)
            ownedString.ToString().ShouldBe("hello");

        using (ownedFunction)
            ownedFunction.Invoke<int>(41).ShouldBe(42);

        using (ownedBuffer)
        {
            ownedBuffer.TryGet(out byte[]? bytes).ShouldBeTrue();
            bytes.ShouldBe(inputBuffer);
        }

        using (ownedUserdata)
        {
            ownedUserdata.TryGetManaged(out Fixtures.ValueUserdata? resolved, out string? error).ShouldBeTrue(error);
            ReferenceEquals(inputUserdata, resolved).ShouldBeTrue();
        }
    }

    [Fact]
    public void ReturningTable_ShouldWorkInsideManagedCallback()
    {
        using LuauFunction func = _state.CreateFunctionBuilder(args =>
        {
            if (!args.TryValidateArgumentCount(0, out string? error))
                return LuauReturn.Error(error);
            using LuauTable x = _state.CreateTable();
            x.Set("value", 99);
            return LuauReturn.Ok(x);
        });

        _state.Globals.Set("make_table", func);
        _state.Load("result = make_table().value").Execute();

        _state.Globals.GetNumber("result").ShouldBe(99);
    }

    [Fact]
    public void ReturningBorrowedTable_FromCoroutine_ShouldReturnArgument()
    {
        using LuauFunction echoTable = _state.CreateFunctionBuilder(static args =>
        {
            if (!args.TryReadLuauTable(1, out LuauTableView table, out string? error))
                return LuauReturn.Error(error);

            return LuauReturn.Ok(table);
        });

        _state.Globals.Set("echo_table", echoTable);

        (int value, bool sameTable) = _state
            .Load(
                """
                local co = coroutine.create(function()
                  local input = { value = 42 }
                  local output = echo_table(input)
                  return output.value, output == input
                end)
                local ok, value, sameTable = coroutine.resume(co)
                if not ok then error(value) end
                return value, sameTable
                """
            )
            .Execute<int, bool>();

        value.ShouldBe(42);
        sameTable.ShouldBeTrue();
    }

    [Fact]
    public void ReturningTable_ShouldWorkInsideManagedCallback2()
    {
        using LuauTable x = _state.CreateTable();
        x.Set("value", 99);

        using LuauFunction func = _state.CreateFunctionBuilder(args =>
        {
            if (!args.TryValidateArgumentCount(0, out var error))
                return LuauReturn.Error(error);
            return LuauReturn.Ok(x);
        });

        _state.Globals.Set("make_table", func);
        _state.Load("result = make_table().value").Execute();

        _state.Globals.GetNumber("result").ShouldBe(99);
        x.GetNumber("value").ShouldBe(99);
    }

    [Fact]
    public void ReturningBuffer_ShouldWorkInsideManagedCallback()
    {
        using LuauFunction func = _state.CreateFunctionBuilder(args =>
        {
            if (!args.TryValidateArgumentCount(0, out string? error))
                return LuauReturn.Error(error);
            using LuauBuffer x = _state.CreateBuffer([0x01, 0x02, 0x03]);
            return LuauReturn.Ok(x);
        });

        _state.Globals.Set("make_buffer", func);
        _state.Load("result = make_buffer()").Execute();

        _state.Globals.TryGetLuauValue("result", out LuauValue result).ShouldBeTrue();
        using (result)
        {
            result.TryGet(out byte[]? bytes).ShouldBeTrue();
            bytes.ShouldBe([0x01, 0x02, 0x03]);
        }
    }

    [Fact]
    public void CallbackException_WhoseMessageThrows_ShouldStillBeALuaError()
    {
        using LuauFunction fail = _state.CreateFunctionBuilder(_ => throw new BrokenMessageException());
        _state.Globals.Set("fail", fail);

        LuaException exception = Should.Throw<LuaException>(() => _state.Load("fail()").Execute());

        exception.Message.ShouldContain(nameof(BrokenMessageException));
        exception.Message.ShouldContain("the exception message is not available");
    }

    [Fact]
    public void CallbackError_WithAVeryLongMessage_ShouldReachTheScript()
    {
        string message = new('x', 1024 * 1024);
        using LuauFunction fail = _state.CreateFunctionBuilder(_ => LuauReturn.Error(message));
        _state.Globals.Set("fail", fail);

        int length = _state.Load("local _, err = pcall(fail) return #err").Execute<int>();

        length.ShouldBeGreaterThanOrEqualTo(message.Length);
    }

    private sealed class BrokenMessageException : Exception
    {
        public override string Message => throw new InvalidOperationException("no message today");
    }

    public void Dispose()
    {
        _state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(1UL);
        _state.Dispose();
    }
}

internal sealed class ArgsUserdataA : ILuauUserData<ArgsUserdataA>
{
    public int Value { get; set; }

    public static LuauReturnSingle OnIndex(ArgsUserdataA self, in LuauState state, in ReadOnlySpan<char> fieldName) =>
        LuauReturnSingle.NotHandled;

    public static LuauOutcome OnSetIndex(ArgsUserdataA self, LuauArgsSingle args, in ReadOnlySpan<char> fieldName) =>
        LuauOutcome.NotHandledError;

    public static LuauReturn OnMethodCall(
        ArgsUserdataA self,
        LuauArgs functionArgs,
        in ReadOnlySpan<char> methodName
    ) => LuauReturn.NotHandledError;

    public static implicit operator IntoLuau(ArgsUserdataA value) => IntoLuau.FromUserdata(value);
}

internal sealed class ArgsUserdataB : ILuauUserData<ArgsUserdataB>
{
    public static LuauReturnSingle OnIndex(ArgsUserdataB self, in LuauState state, in ReadOnlySpan<char> fieldName) =>
        LuauReturnSingle.NotHandled;

    public static LuauOutcome OnSetIndex(ArgsUserdataB self, LuauArgsSingle args, in ReadOnlySpan<char> fieldName) =>
        LuauOutcome.NotHandledError;

    public static LuauReturn OnMethodCall(
        ArgsUserdataB self,
        LuauArgs functionArgs,
        in ReadOnlySpan<char> methodName
    ) => LuauReturn.NotHandledError;

    public static implicit operator IntoLuau(ArgsUserdataB value) => IntoLuau.FromUserdata(value);
}

internal sealed class GeneratedReturnUserdata : ILuauUserData<GeneratedReturnUserdata>
{
    public static LuauReturnSingle OnIndex(
        GeneratedReturnUserdata self,
        in LuauState state,
        in ReadOnlySpan<char> fieldName
    ) => LuauReturnSingle.NotHandled;

    public static LuauOutcome OnSetIndex(
        GeneratedReturnUserdata self,
        LuauArgsSingle args,
        in ReadOnlySpan<char> fieldName
    ) => LuauOutcome.NotHandledError;

    public static LuauReturn OnMethodCall(
        GeneratedReturnUserdata self,
        LuauArgs functionArgs,
        in ReadOnlySpan<char> methodName
    ) => LuauReturn.NotHandledError;
}
