using Shouldly;

namespace Darp.Luau.Tests;

public sealed class ChunkTests : IDisposable
{
    private readonly LuauState _lua = new();

    [Fact]
    public void Execute_Simple()
    {
        LuauChunk chunk = _lua.Load("result = 1");
        chunk.Execute();

        _lua.Globals.Get<double>("result").ShouldBe(1);
    }

    [Fact]
    public void Execute_Simple_Arguments()
    {
        LuauChunk chunk = _lua.Load("result = ...");
        chunk.Execute(6);

        _lua.Globals.Get<double>("result").ShouldBe(6);
    }

    [Fact]
    public void Execute_Returns()
    {
        LuauChunk chunk = _lua.Load("return 1 + 1");
        int result = chunk.Execute<int>();

        result.ShouldBe(2);
    }

    [Fact]
    public void Execute_Returns2()
    {
        LuauChunk chunk = _lua.Load("return 1, 2");
        (int result1, int result2) = chunk.Execute<int, int>();

        result1.ShouldBe(1);
        result2.ShouldBe(2);
    }

    [Fact]
    public void Execute_Arguments()
    {
        LuauChunk chunk = _lua.Load(
            """
            local a, b = ...
            return a + b
            """
        );
        int result = chunk.Execute<int>(1, 2);

        result.ShouldBe(3);
    }

    [Fact]
    public void ToFunction_Simple()
    {
        LuauChunk chunk = _lua.Load("return 1 + 1");
        using LuauFunction func = chunk.ToFunction();

        int result = func.Invoke<int>();

        result.ShouldBe(2);
    }

    [Fact]
    public void ToFunction_Arguments()
    {
        LuauChunk chunk = _lua.Load(
            """
            local a, b = ...
            return a + b
            """
        );
        using LuauFunction func = chunk.ToFunction();

        int result = func.Invoke<int>(1, 2);

        result.ShouldBe(3);
    }

    [Fact]
    public void ExecuteMulti_ReturnsAllValues()
    {
        LuauChunk chunk = _lua.Load("return 10, 'hello', true");
        LuauValue[] values = chunk.ExecuteMulti();

        values.Length.ShouldBe(3);

        using LuauValue value1 = values[0];
        using LuauValue value2 = values[1];
        using LuauValue value3 = values[2];

        value1.TryGet(out int number).ShouldBeTrue();
        value2.TryGet(out string? text).ShouldBeTrue();
        value3.TryGet(out bool flag).ShouldBeTrue();

        number.ShouldBe(10);
        text.ShouldBe("hello");
        flag.ShouldBeTrue();
    }

    [Fact]
    public void ExecuteMulti_WithOwnedReference_ClonesOwnership()
    {
        LuauChunk chunk = _lua.Load("return { value = 42 }, 5");
        LuauValue[] values = chunk.ExecuteMulti();

        values.Length.ShouldBe(2);

        using LuauValue tableValue = values[0];
        using LuauValue countValue = values[1];

        ulong beforeClone = _lua.MemoryStatistics.ActiveRegistryReferences;
        tableValue.TryGet(out LuauTable table).ShouldBeTrue();
        using (table)
        {
            _lua.MemoryStatistics.ActiveRegistryReferences.ShouldBe(beforeClone + 1);
            table.Get<double>("value").ShouldBe(42);
        }
        _lua.MemoryStatistics.ActiveRegistryReferences.ShouldBe(beforeClone);

        countValue.TryGet(out int count).ShouldBeTrue();
        count.ShouldBe(5);
    }

    [Fact]
    public void Load_ByteSpan_Execute()
    {
        LuauChunk chunk = _lua.Load("result = 42"u8);
        chunk.Execute();

        _lua.Globals.Get<double>("result").ShouldBe(42);
    }

    [Fact]
    public void Load_ByteSpan_ExecuteTyped()
    {
        LuauChunk chunk = _lua.Load("return 42"u8);
        int result = chunk.Execute<int>();

        result.ShouldBe(42);
    }

    [Fact]
    public void Load_ByteSpan_ExecuteMulti()
    {
        LuauChunk chunk = _lua.Load("return 1, 2"u8);
        LuauValue[] values = chunk.ExecuteMulti();

        values.Length.ShouldBe(2);

        using LuauValue value1 = values[0];
        using LuauValue value2 = values[1];

        value1.TryGet(out int first).ShouldBeTrue();
        value2.TryGet(out int second).ShouldBeTrue();

        first.ShouldBe(1);
        second.ShouldBe(2);
    }

    [Fact]
    public void WithName_UsesChunkNameInError()
    {
        LuauChunk chunk = _lua.Load("error('boom')").WithName("@named_chunk.luau");

        LuaException exception;
        try
        {
            chunk.Execute();
            throw new ShouldAssertException("Expected LuaException to be thrown.");
        }
        catch (LuaException ex)
        {
            exception = ex;
        }

        exception.Message.ShouldContain("named_chunk.luau");
        exception.Message.ShouldContain("boom");
    }

    [Fact]
    public void WithName_WorksWithByteSource()
    {
        LuauChunk chunk = _lua.Load("error('boom')"u8).WithName("@named_utf8_chunk.luau");

        LuaException exception;
        try
        {
            chunk.Execute();
            throw new ShouldAssertException("Expected LuaException to be thrown.");
        }
        catch (LuaException ex)
        {
            exception = ex;
        }

        exception.Message.ShouldContain("named_utf8_chunk.luau");
    }

    [Fact]
    public void WithEnvironment_ReadsGlobalsAndWritesLocals()
    {
        _lua.Globals.Set("baseValue", 40);

        using LuauTable environment = _lua.CreateEnvironment();
        int result = _lua.Load(
                """
                computed = baseValue + 2
                return computed
                """
            )
            .WithEnvironment(environment)
            .Execute<int>();

        result.ShouldBe(42);
        environment.Get<double>("computed").ShouldBe(42);
        _lua.Globals.Get<double>("baseValue").ShouldBe(40);
        _lua.Globals.ContainsKey("computed").ShouldBeFalse();
    }

    [Fact]
    public void WithEnvironment_LoadError_ShouldThrowLuaExceptionAndKeepStackUsable()
    {
        using LuauTable environment = _lua.CreateEnvironment();

        LuaException exception = Should.Throw<LuaException>(() =>
            _lua.Load("return function(").WithEnvironment(environment).Execute()
        );
        exception.Message.ShouldContain("luau_load");

        int result = _lua.Load("return 42").WithEnvironment(environment).Execute<int>();
        result.ShouldBe(42);
    }

    [Fact]
    public void CreateEnvironment_Sets_G_ToEnvironment()
    {
        using LuauTable environment = _lua.CreateEnvironment();

        _lua.Load("_G.answer = 21").WithEnvironment(environment).Execute();

        environment.Get<double>("answer").ShouldBe(21);
        _lua.Globals.ContainsKey("answer").ShouldBeFalse();
    }

    [Fact]
    public void ToFunction_PreservesEnvironment()
    {
        _lua.Globals.Set("step", 3);

        using LuauTable environment = _lua.CreateEnvironment();
        using LuauFunction function = _lua.Load(
                """
                counter = (counter or 0) + step
                return counter
                """
            )
            .WithEnvironment(environment)
            .ToFunction();

        function.Invoke<int>().ShouldBe(3);
        function.Invoke<int>().ShouldBe(6);

        environment.Get<double>("counter").ShouldBe(6);
        _lua.Globals.ContainsKey("counter").ShouldBeFalse();
    }

    [Fact]
    public void WithEnvironment_FromDifferentState_Throws()
    {
        using var otherState = new LuauState();
        using LuauTable environment = otherState.CreateEnvironment();

        InvalidOperationException exception = Should.Throw<InvalidOperationException>(() =>
            _lua.Load("return 1").WithEnvironment(environment)
        );

        exception.Message.ShouldContain("same LuauState");
    }

    [Fact]
    public void WithEnvironment_DisposedEnvironment_Throws()
    {
        LuauTable environment = _lua.CreateEnvironment();
        environment.Dispose();

        Should.Throw<ObjectDisposedException>(() => _lua.Load("return 1").WithEnvironment(environment));
    }

    [Fact]
    public void Execute_WithDisposedConfiguredEnvironment_Throws()
    {
        LuauTable environment = _lua.CreateEnvironment();
        LuauChunk chunk = _lua.Load("return 1").WithEnvironment(environment);
        environment.Dispose();

        try
        {
            _ = chunk.Execute<int>();
            throw new ShouldAssertException("Expected ObjectDisposedException to be thrown.");
        }
        catch (ObjectDisposedException) { }
    }

    [Fact]
    public void ToFunction_WithDisposedConfiguredEnvironment_Throws()
    {
        LuauTable environment = _lua.CreateEnvironment();
        LuauChunk chunk = _lua.Load("return 1").WithEnvironment(environment);
        environment.Dispose();

        try
        {
            using LuauFunction _ = chunk.ToFunction();
            throw new ShouldAssertException("Expected ObjectDisposedException to be thrown.");
        }
        catch (ObjectDisposedException) { }
    }

    [Fact]
    public void ToFunction_DisposingEnvironmentRefAfterwards_ShouldWork()
    {
        LuauTable environment = _lua.CreateEnvironment();
        LuauChunk chunk = _lua.Load("return 1").WithEnvironment(environment);

        using LuauFunction func = chunk.ToFunction();
        environment.Dispose();

        func.Invoke<int>().ShouldBe(1);
    }

    [Fact]
    public void Environments_WorkWithSimpleTables()
    {
        using LuauTable environment = _lua.CreateTable();
        environment.Set("my_value", 42);
        LuauChunk chunk = _lua.Load("return my_value").WithEnvironment(environment);

        using LuauFunction func = chunk.ToFunction();

        func.Invoke<int>().ShouldBe(42);
    }

    public void Dispose()
    {
        _lua.MemoryStatistics.ActiveRegistryReferences.ShouldBe(1U);
        _lua.Dispose();
    }
}
