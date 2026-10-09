using Darp.Luau.Internal.Require;
using Darp.Luau.Tests.Require;
using Shouldly;

namespace Darp.Luau.Tests;

public sealed class StandardLibraryAndModuleLoadingTests : IDisposable
{
    private readonly List<LuauState> _states = [];

    [Fact]
    public void BuiltinLibraries_None_ShouldLoadNoStandardGlobals()
    {
        LuauState state = CreateState(LuauLibraries.None);

        state.EnabledLibraries.ShouldBe(LuauLibraries.None);
        state.Globals.ContainsKey("pcall").ShouldBeFalse();
        state.Globals.ContainsKey("table").ShouldBeFalse();
        state.Globals.ContainsKey("error").ShouldBeFalse();
        state.Globals.ContainsKey("type").ShouldBeFalse();
        state.Globals.ContainsKey("_G").ShouldBeFalse();
        state.Globals.ContainsKey("math").ShouldBeFalse();
    }

    [Fact]
    public void BuiltinLibraries_ShouldLoadOnlyRequestedOptionalLibraries()
    {
        LuauState state = CreateState(LuauLibraries.Math | LuauLibraries.Vector);

        state.EnabledLibraries.ShouldBe(LuauLibraries.Math | LuauLibraries.Vector);
        state.Globals.ContainsKey("math").ShouldBeTrue();
        state.Globals.ContainsKey("vector").ShouldBeTrue();
        state.Globals.ContainsKey("table").ShouldBeFalse();
        state.Globals.ContainsKey("pcall").ShouldBeFalse();
        state.Globals.ContainsKey("utf8").ShouldBeFalse();
    }

    [Fact]
    public void CreateFunctionManual_ShouldWorkUnderNone()
    {
        LuauState state = CreateState(LuauLibraries.None);
        using LuauFunction add = state.CreateFunctionManual(static args =>
        {
            if (!args.TryReadNumber(1, out int a, out string? error))
                return LuauReturn.Error(error);
            if (!args.TryReadNumber(2, out int b, out error))
                return LuauReturn.Error(error);
            return LuauReturn.Ok(a + b);
        });
        state.Globals.Set("add", add);

        int result = state.Load("return add(4, 7)").Execute<int>();

        result.ShouldBe(11);
    }

    [Fact]
    public void CreateFunctionManual_Error_ShouldThrowUnderNone()
    {
        LuauState state = CreateState(LuauLibraries.None);
        using LuauFunction fail = state.CreateFunctionManual(static _ => LuauReturn.Error("boom from callback"));
        state.Globals.Set("fail", fail);

        LuaException exception = Should.Throw<LuaException>(() => state.Load("return fail()").Execute());

        exception.Message.ShouldContain("boom from callback");
    }

    [Fact]
    public void HostModuleRequire_ShouldWorkUnderNone()
    {
        LuauState state = CreateState(LuauLibraries.None);
        state.RegisterModule("game", static (_, in module) => module.Set("answer", 42));

        int result = state.Load("""return require("game").answer""").Execute<int>();

        result.ShouldBe(42);
    }

    [Fact]
    public void ScriptModuleRequire_ShouldWorkUnderNone()
    {
        var fs = new FakeFileSystem([
            ("./main.luau", """return require("./dependency").answer"""),
            ("./dependency.luau", "return { answer = 42 }"),
        ]);

        LuauState state = CreateState(LuauLibraries.None, fs);
        state.EnableScriptModules();

        int result = state.LoadFile("./main.luau").Execute<int>();

        result.ShouldBe(42);
    }

    [Fact]
    public void ScriptModule_ShouldCallHostModuleFunctionUnderNone()
    {
        var fs = new FakeFileSystem([
            (
                "./main.luau",
                """
                local game = require("game")
                return game.add(40, 2)
                """
            ),
        ]);

        LuauState state = CreateState(LuauLibraries.None, fs);
        state.RegisterModule(
            "game",
            static (lua, in module) =>
            {
                using LuauFunction add = lua.CreateFunctionManual(static args =>
                {
                    if (!args.TryReadNumber(1, out int a, out string? error))
                        return LuauReturn.Error(error);
                    if (!args.TryReadNumber(2, out int b, out error))
                        return LuauReturn.Error(error);
                    return LuauReturn.Ok(a + b);
                });
                module.Set("add", add);
            }
        );
        state.EnableScriptModules();

        int result = state.LoadFile("./main.luau").Execute<int>();

        result.ShouldBe(42);
    }

    [Fact]
    public void CustomModule_ShouldBeAvailableThroughRequire()
    {
        LuauState state = CreateState();
        state.RegisterModule(
            "game",
            static (state, in module) =>
            {
                module.Set("answer", 42);

                using LuauFunction add = state.CreateFunctionManual(static args =>
                {
                    if (!args.TryReadNumber(1, out int a, out string? error))
                        return LuauReturn.Error(error);
                    if (!args.TryReadNumber(2, out int b, out error))
                        return LuauReturn.Error(error);
                    return LuauReturn.Ok(a + b);
                });
                module.Set("add", add);
            }
        );
        state
            .Load(
                """
                local game = require("game")
                sum = game.add(4, 7)
                answer = game.answer
                """
            )
            .Execute();

        state.Globals.TryGet("sum", out int sum).ShouldBeTrue();
        sum.ShouldBe(11);
        state.Globals.TryGet("answer", out int answer).ShouldBeTrue();
        answer.ShouldBe(42);
    }

    [Fact]
    public void CustomModule_Conflict_ShouldThrow()
    {
        LuauState state = CreateState();
        state.RegisterModule("game", static (_, in _) => { });

        Should.Throw<InvalidOperationException>(() => state.RegisterModule("game", static (_, in _) => { }));
    }

    [Fact]
    public void CustomModule_ShouldLoadOnceAndCacheTable()
    {
        LuauState state = CreateState();
        int loadCount = 0;

        state.RegisterModule(
            "game",
            (_, in module) =>
            {
                loadCount++;
                module.Set("answer", 99);
            }
        );
        state
            .Load(
                """
                local first = require("game")
                local second = require("game")
                same_table = first == second
                answer = second.answer
                """
            )
            .Execute();

        state.Globals.TryGet("answer", out int answer).ShouldBeTrue();
        answer.ShouldBe(99);
        state.Globals.TryGet("same_table", out bool sameTable).ShouldBeTrue();
        sameTable.ShouldBeTrue();
        loadCount.ShouldBe(1);
    }

    [Fact]
    public void RegisterModule_Generic_ShouldUseModuleContract()
    {
        LuauState state = CreateState();
        var module = new TestModule(123);

        state.RegisterModule<TestModule>(module);
        state.Load("answer = require('typed').answer").Execute();

        state.Globals.TryGet("answer", out int answer).ShouldBeTrue();
        answer.ShouldBe(123);
        module.LoadCount.ShouldBe(1);
    }

    [Theory]
    [InlineData("./game")]
    [InlineData("../game")]
    [InlineData("@game")]
    [InlineData("/game")]
    [InlineData("\\game")]
    [InlineData("game/module")]
    [InlineData("game\\module")]
    public void RegisterModule_ShouldRejectScriptModuleNames(string name)
    {
        LuauState state = CreateState();

        Should.Throw<ArgumentException>(() => state.RegisterModule(name, static (_, in _) => { }));
    }

    [Fact]
    public void Require_UnregisteredBareModule_ShouldFail()
    {
        LuauState state = CreateState();
        state.RegisterModule("registered", static (_, in _) => { });

        (bool success, string error) = state.Load("return pcall(require, 'missing')").Execute<bool, string>();

        success.ShouldBeFalse();
        error.ShouldContain("module 'missing' is not registered");
    }

    public void Dispose()
    {
        foreach (LuauState state in _states)
        {
            if (!state.IsDisposed)
                state.Dispose();
            state.MemoryStatistics.ActiveRegistryReferences.ShouldBe(0UL);
        }
    }

    private LuauState CreateState(LuauLibraries libraries = LuauLibraries.All, ILuauFileSystem? fileSystem = null)
    {
        LuauState state = new(libraries, fileSystem);
        _states.Add(state);
        return state;
    }

    private sealed class TestModule(int answer) : ILuauModule<TestModule>
    {
        public static string ModuleName => "typed";

        public int LoadCount { get; private set; }

        public void OnLoad(LuauState lua, in LuauTable module)
        {
            LoadCount++;
            module.Set("answer", answer);
        }
    }
}
