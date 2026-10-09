using Darp.Luau.Tests.Fixtures;
using Darp.Luau.Tests.Require;
using Shouldly;

namespace Darp.Luau.Tests;

/// <summary> A state whose globals and libraries are read-only, and whose scripts each get globals of their own. </summary>
public sealed class SandboxTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    /// <summary> A state with a host table in its globals, sealed after that. </summary>
    private static LuauState CreateSandboxed()
    {
        var state = new LuauState();
        using LuauTable host = state.Load("return { value = 1, nested = { value = 1 } }").Execute<LuauTable>();
        state.Globals.Set("host", host);
        state.EnableSandbox();
        return state;
    }

    [Fact]
    public void State_ShouldNotBeSandboxedUnlessAsked()
    {
        using var state = new LuauState();

        state.IsSandboxed.ShouldBeFalse();
        state.Load("math.pi = 3; answer = 42").Execute();
        state.Globals.GetNumber("answer").ShouldBe(42);
    }

    [Fact]
    public void Script_ShouldKeepTheGlobalsItAssignsToItself()
    {
        using LuauState state = CreateSandboxed();

        int answer = state
            .Load("answer = 41; function next() answer = answer + 1; return answer end; return next()")
            .Execute<int>();

        answer.ShouldBe(42);
        state.Load("return answer == nil and next ~= nil and _G.answer == nil").Execute<bool>().ShouldBeTrue();
        state.Globals.ContainsKey("answer").ShouldBeFalse();
    }

    [Fact]
    public void Script_ShouldBeAbleToGiveANameAnotherValueForItself()
    {
        using LuauState state = CreateSandboxed();

        int own = state.Load("math = { floor = function() return 42 end }; return math.floor(1.5)").Execute<int>();

        own.ShouldBe(42);
        state.Load("return math.floor(1.5)").Execute<int>().ShouldBe(1);
    }

    [Theory]
    [InlineData("_G.answer = 42")]
    [InlineData("rawset(_G, 'answer', 42)")]
    [InlineData("setmetatable(_G, {})")]
    [InlineData("math.floor = nil")]
    [InlineData("rawset(math, 'floor', nil)")]
    [InlineData("setmetatable(math, {})")]
    [InlineData("string.format = nil")]
    [InlineData("getmetatable('').__index = {}")]
    [InlineData("host.value = 2")]
    [InlineData("table.clear(host)")]
    public void Script_ShouldNotChangeTheGlobalsOrALibrary(string script)
    {
        using LuauState state = CreateSandboxed();

        LuaException exception = Should.Throw<LuaException>(() => state.Load(script).Execute());

        exception.Message.ShouldContain("readonly", Case.Insensitive);
        // Everything is as it was, and the state still runs scripts.
        state.Load("return math.floor(host.value + 0.5) + #string.format('%d', 1)").Execute<int>().ShouldBe(2);
        state.Globals.ContainsKey("answer").ShouldBeFalse();
    }

    [Fact]
    public void Host_ShouldNotChangeTheGlobalsOrAddToThemAfterwards()
    {
        using var state = new LuauState(LuauLibraries.Minimal);
        state.EnableSandbox();
        state.EnableSandbox();

        state.IsSandboxed.ShouldBeTrue();
        Should.Throw<LuaException>(() => state.Globals.Set("answer", 42));
        Should.Throw<InvalidOperationException>(() => state.LoadStandardLibraries(LuauLibraries.Math));
        Should.Throw<InvalidOperationException>(() => state.EnableScriptModules());
        Should.Throw<InvalidOperationException>(() =>
            state.RegisterModule("late", static (LuauState _, in LuauTable _) => { })
        );
        // A library that is there already is no change.
        state.LoadStandardLibraries(LuauLibraries.Base);
        state.Load("return 1").Execute<int>().ShouldBe(1);
    }

    [Fact]
    public void Environment_ShouldHoldTheGlobalsOfItsScripts()
    {
        using LuauState state = CreateSandboxed();
        using LuauTable first = state.CreateEnvironment();
        using LuauTable second = state.CreateEnvironment();

        state
            .Load("counter = (counter or 0) + host.value; function double(x) return x * 2 end")
            .WithEnvironment(first)
            .Execute();
        int doubled = state.Load("counter = counter + 1; return double(counter)").WithEnvironment(first).Execute<int>();

        doubled.ShouldBe(4);
        first.GetNumber("counter").ShouldBe(2);
        state.Load("return counter == nil and double == nil").WithEnvironment(second).Execute<bool>().ShouldBeTrue();
        state.Globals.ContainsKey("counter").ShouldBeFalse();
    }

    [Fact]
    public void Environment_ShouldBeAbleToReplaceALibraryForItsOwnScriptsOnly()
    {
        using LuauState state = CreateSandboxed();
        using LuauTable environment = state.CreateEnvironment();
        using LuauFunction floor = state.Load("return math.floor(1.5)").WithEnvironment(environment).ToFunction();

        floor.Invoke<int>().ShouldBe(1);
        state.Load("math = { floor = function() return 42 end }").WithEnvironment(environment).Execute();

        // Also for a function that was loaded before: an environment is looked up every time.
        floor.Invoke<int>().ShouldBe(42);
        state.Load("return math.floor(1.5)").WithEnvironment(environment).Execute<int>().ShouldBe(42);
        state.Load("return math.floor(1.5)").Execute<int>().ShouldBe(1);
    }

    [Fact]
    public void Environment_ShouldGiveItsFirstScriptWhatTheHostPutsIntoItLater()
    {
        using LuauState state = CreateSandboxed();
        using LuauTable environment = state.CreateEnvironment();
        using LuauFunction read = state.Load("return step or 0, host.value").WithEnvironment(environment).ToFunction();

        read.Invoke<int, int>().ShouldBe((0, 1));
        environment.Set("step", 1);
        read.Invoke<int, int>().ShouldBe((1, 1));
        environment.Set("step", 2);
        read.Invoke<int, int>().ShouldBe((2, 1));
    }

    [Fact]
    public void Environment_ShouldReadAnObjectOfTheHostEveryTime()
    {
        using var state = new LuauState();
        var shared = new Live { Value = 1 };
        using LuauUserdata sharedUserdata = state.GetOrCreateUserdata(shared);
        state.Globals.Set("shared", sharedUserdata);
        state.EnableSandbox();
        var own = new Live { Value = 10 };
        using LuauUserdata ownUserdata = state.GetOrCreateUserdata(own);
        using LuauTable environment = state.CreateEnvironment();
        environment.Set("own", ownUserdata);

        using LuauFunction sum = state
            .Load("return shared.value + own.value")
            .WithEnvironment(environment)
            .ToFunction();

        (shared.Reads + own.Reads).ShouldBe(0);
        sum.Invoke<int>().ShouldBe(11);
        shared.Value = 2;
        own.Value = 20;
        sum.Invoke<int>().ShouldBe(22);
    }

    [Fact]
    public void Environment_ShouldSeeWhatIsWrittenIntoItAfterItsScriptWasLoaded()
    {
        using var state = new LuauState();
        using LuauFunction log = state.CreateFunctionManual(static _ => LuauReturn.Ok(1));
        state.Globals.Set("log", log);
        state.EnableSandbox();
        using LuauTable environment = state.CreateEnvironment();
        using LuauTable handedOut = state.CreateEnvironment();
        using LuauFunction call = state.Load("return log()").WithEnvironment(environment).ToFunction();
        using LuauFunction floor = state
            .Load("local self = ...; self.math = { floor = function() return 42 end }; return math.floor(1.5)")
            .WithEnvironment(handedOut)
            .ToFunction();

        // By the host, with a name of the globals of the state.
        call.Invoke<int>().ShouldBe(1);
        using LuauFunction other = state.CreateFunctionManual(static _ => LuauReturn.Ok(42));
        environment.Set("log", other);
        call.Invoke<int>().ShouldBe(42);
        // By a script that was handed the environment as a value.
        floor.Invoke<int>(handedOut).ShouldBe(42);
    }

    [Fact]
    public void Environment_ShouldBeTheSameWhetherItWasCreatedBeforeOrAfter()
    {
        using var state = new LuauState();
        using LuauTable before = state.CreateEnvironment();
        state.EnableSandbox();
        using LuauTable after = state.CreateEnvironment();

        foreach (LuauTable environment in new[] { before, after })
        {
            state.Load("_G.answer = 42; return answer").WithEnvironment(environment).Execute<int>().ShouldBe(42);
            environment.GetNumber("answer").ShouldBe(42);
        }
        state.Globals.ContainsKey("answer").ShouldBeFalse();
    }

    [Fact]
    public void Environment_ShouldAcceptAnyTableOfAScript()
    {
        using LuauState state = CreateSandboxed();
        using LuauTable environment = state
            .Load("return setmetatable({}, table.freeze({ __index = _G, __darp_loaded = false }))")
            .Execute<LuauTable>();

        state.Load("mine = host.value + 1; return mine").WithEnvironment(environment).Execute<int>().ShouldBe(2);
    }

    [Theory]
    [InlineData("floor = 7; return math.floor(1.5)")]
    [InlineData("local floor = math.floor; return floor(1.5)")]
    public void Script_ShouldNotReachALibraryThatWasNotLoaded_WhateverElseItDoes(string script)
    {
        using var state = new LuauState(LuauLibraries.Minimal);
        state.EnableSandbox();

        Should.Throw<LuaException>(() => state.Load(script).Execute());
    }

    [Fact]
    public void Script_ShouldCallAReplacedBuiltinAlsoWhenItHasAGlobalOfThatName()
    {
        using var state = new LuauState();
        using LuauFunction floor = state.CreateFunctionManual(static _ => LuauReturn.Ok(42));
        using (LuauTable math = state.Globals.GetLuauTable("math"))
            math.Set("floor", floor);
        state.EnableSandbox();

        state.Load("floor = 7; return math.floor(1.5)").Execute<int>().ShouldBe(42);
    }

    [Theory]
    [InlineData("next", "for _, value in next, { a = 1 } do total += value end")]
    [InlineData("pairs", "for _, value in pairs({ a = 1 }) do total += value end")]
    [InlineData("ipairs", "for _, value in ipairs({ 1 }) do total += value end")]
    public void Script_ShouldIterateWithAReplacedIterator(string name, string loop)
    {
        using var state = new LuauState();
        // Every one of them makes the loop see the single value 42.
        state
            .Load(
                $$"""
                local function once(_, key) if key == nil then return 'a', 42 end end
                if '{{name}}' == 'next' then next = once else {{name}} = function(t) return once, t, nil end end
                """
            )
            .Execute();
        state.EnableSandbox();

        state.Load($"local total = 0; {loop}; return total").Execute<int>().ShouldBe(42);
    }

    [Fact]
    public void FunctionLoadedBefore_ShouldBehaveTheSameAfterwards()
    {
        using var state = new LuauState();
        using LuauFunction floor = state.CreateFunctionManual(static _ => LuauReturn.Ok(42));
        using (LuauTable math = state.Globals.GetLuauTable("math"))
            math.Set("floor", floor);
        state.Load("rawset = nil").Execute();
        using LuauFunction callFloor = state.Load("return math.floor(1.5)").ToFunction();
        using LuauFunction callRawset = state.Load("rawset({}, 'a', 1)").ToFunction();
        callFloor.Invoke<int>().ShouldBe(42);
        Should.Throw<LuaException>(() => callRawset.Invoke());

        state.EnableSandbox();

        callFloor.Invoke<int>().ShouldBe(42);
        Should.Throw<LuaException>(() => callRawset.Invoke());
    }

    [Fact]
    public void GlobalsThatComputeTheirValues_ShouldBeAskedEveryTime()
    {
        using var state = new LuauState();
        int current = 1;
        int asked = 0;
        using LuauFunction compute = state.CreateFunctionManual(_ =>
        {
            asked++;
            return LuauReturn.Ok(current);
        });
        state.Globals.Set("compute", compute);
        state
            .Load(
                "setmetatable(_G, { __index = function(_, name) if name == 'dynamic' then return compute() end end })"
            )
            .Execute();
        state.EnableSandbox();

        using LuauFunction read = state.Load("return dynamic").ToFunction();

        asked.ShouldBe(0);
        read.Invoke<int>().ShouldBe(1);
        current = 42;
        read.Invoke<int>().ShouldBe(42);
    }

    [Fact]
    public void ScriptModule_ShouldReadTheGlobalsOfTheStateWhateverTheMainThreadHas()
    {
        var fileSystem = new FakeFileSystem([
            ("./main.luau", "return require('./token')"),
            ("./token.luau", "return token"),
        ]);
        using var state = new LuauState(LuauLibraries.All, fileSystem);
        state.EnableScriptModules();
        state.Globals.Set("token", 1);
        state.Load("setfenv(0, setmetatable({ token = 99 }, { __index = _G }))").Execute();

        state.LoadFile("./main.luau").Execute<int>().ShouldBe(1);
    }

    [Fact]
    public void Script_ShouldNotReachALibraryThatWasNotLoaded()
    {
        using var state = new LuauState(LuauLibraries.Minimal);
        state.EnableSandbox();

        // The compiler knows math.floor by its name. The state has no math library.
        Should.Throw<LuaException>(() => state.Load("return math.floor(1.5)").Execute());
        state.Load("return type(1)").Execute<string>().ShouldBe("number");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScriptModule_ShouldReadHostObjectsEveryTime(bool sandboxed)
    {
        var fileSystem = new FakeFileSystem([
            ("./main.luau", "return require('./reader')"),
            (
                "./reader.luau",
                "return { read = function() return live.value end, floor = function() return math.floor(1.5) end }"
            ),
        ]);
        using var state = new LuauState(LuauLibraries.All, fileSystem);
        state.EnableScriptModules();
        var live = new Live { Value = 1 };
        using LuauUserdata liveUserdata = state.GetOrCreateUserdata(live);
        state.Globals.Set("live", liveUserdata);
        using LuauFunction floor = state.CreateFunctionManual(static _ => LuauReturn.Ok(42));
        using (LuauTable math = state.Globals.GetLuauTable("math"))
            math.Set("floor", floor);
        if (sandboxed)
            state.EnableSandbox();

        using LuauTable reader = state.LoadFile("./main.luau").Execute<LuauTable>();
        using LuauFunction read = reader.GetLuauFunction("read");
        using LuauFunction moduleFloor = reader.GetLuauFunction("floor");

        live.Reads.ShouldBe(0);
        read.Invoke<int>().ShouldBe(1);
        live.Value = 7;
        read.Invoke<int>().ShouldBe(7);
        moduleFloor.Invoke<int>().ShouldBe(42);
    }

    [Fact]
    public void Freeze_ShouldOnlyReachTheTablesDirectlyInTheGlobals()
    {
        using LuauState state = CreateSandboxed();

        state.Load("host.nested.value = 2").Execute();

        state.Load("return host.nested.value").Execute<int>().ShouldBe(2);
    }

    [Fact]
    public void ScriptModule_ShouldKeepItsGlobalsAndReadTheSharedOnes()
    {
        var fileSystem = new FakeFileSystem([
            ("./main.luau", "local counter = require('./counter'); return counter.next(), leaked == nil"),
            ("./counter.luau", "leaked = host.value; return { next = function() return leaked + 1 end }"),
            ("./vandal.luau", "math.floor = nil"),
            ("./run_vandal.luau", "require('./vandal')"),
        ]);
        var state = new LuauState(LuauLibraries.All, fileSystem);
        using var _ = state;
        using LuauTable host = state.Load("return { value = 1 }").Execute<LuauTable>();
        state.Globals.Set("host", host);
        state.EnableScriptModules();
        state.EnableSandbox();

        (int next, bool stayedInTheModule) = state.LoadFile("./main.luau").Execute<int, bool>();

        next.ShouldBe(2);
        stayedInTheModule.ShouldBeTrue();
        Should
            .Throw<LuaException>(() => state.LoadFile("./run_vandal.luau").Execute())
            .Message.ShouldContain("readonly", Case.Insensitive);
    }

    [Fact]
    public void HostModule_ShouldBeBuiltWhenItIsFirstRequiredAndItsTableStaysWritable()
    {
        using var state = new LuauState();
        state.RegisterModule("game", static (LuauState _, in LuauTable module) => module.Set("answer", 42));
        state.EnableSandbox();
        // 'require' exists, so a later module changes no global.
        state.RegisterModule("late", static (LuauState _, in LuauTable module) => module.Set("value", 1));

        int sum = state
            .Load(
                "local game = require('game'); game.answer = 41; return require('game').answer + require('late').value"
            )
            .Execute<int>();

        sum.ShouldBe(42);
    }

    [Fact]
    public void Userdata_ShouldBeCreatedAndUsedAfterwards()
    {
        using var state = new LuauState();
        using (LuauTable point = state.GetTypeTable<Point>())
            state.Globals.Set("Point", point);
        state.EnableSandbox();
        // A type the state has not seen before it was sealed.
        using LuauUserdata value = state.GetOrCreateUserdata(new ValueUserdata { Value = 7 });
        using LuauTable environment = state.CreateEnvironment();
        environment.Set("value", value);

        (double x, bool isUserdata) = state
            .Load("local sum = Point.new(1, 2) + Point.new(3, 4); return sum.x, typeof(value) == 'userdata'")
            .WithEnvironment(environment)
            .Execute<double, bool>();

        x.ShouldBe(4);
        isUserdata.ShouldBeTrue();
    }

    [Fact]
    public async Task AsyncCallback_ShouldSuspendAScript()
    {
        using var state = new LuauState();
        using LuauFunction later = state.CreateFunction(
            async (int value) =>
            {
                await Task.Yield();
                return value + 1;
            }
        );
        state.Globals.Set("later", later);
        state.EnableSandbox();

        int result = await state.Load("return later(41)").ExecuteAsync<int>([], TestToken);

        result.ShouldBe(42);
    }

    [Fact]
    public async Task Cancellation_ShouldStopALoop()
    {
        using var state = new LuauState();
        using var cts = new CancellationTokenSource();
        using LuauFunction cancel = state.CreateFunctionManual(_ =>
        {
            cts.Cancel();
            return LuauReturn.Ok();
        });
        state.Globals.Set("cancel", cancel);
        state.EnableSandbox();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            state.Load("cancel() while true do end").ExecuteAsync([], cts.Token).AsTask()
        );

        state.Load("return math.abs(-1)").Execute<int>().ShouldBe(1);
    }

    [Fact]
    public void Coroutine_ShouldRunWithTheSharedGlobals()
    {
        using LuauState state = CreateSandboxed();
        using LuauFunction body = state
            .Load("local first = coroutine.yield(host.value); return first + 1")
            .ToFunction();
        using LuauCoroutine coroutine = state.CreateCoroutine(body);

        coroutine.Resume<int>().ShouldBe(1);
        coroutine.Resume<int>(41).ShouldBe(42);
    }

    [Fact]
    public void EnableSandbox_WhenAScriptFrozeTheGlobals_ShouldFailAndChangeNothing()
    {
        using var state = new LuauState();
        state.Load("table.freeze(_G)").Execute();

        Should.Throw<LuaException>(() => state.EnableSandbox());

        state.IsSandboxed.ShouldBeFalse();
        state.Load("return getfenv ~= nil and setfenv ~= nil").Execute<bool>().ShouldBeTrue();
    }

    [Fact]
    public void EnableSandbox_ShouldUseTheGlobalsOfTheStateAndTakeAwayWhatReplacesThem()
    {
        using var state = new LuauState();
        state.Load("setfenv(0, setmetatable({ marker = 'replaced' }, { __index = _G }))").Execute();
        state.Load("return marker").Execute<string>().ShouldBe("replaced");

        state.EnableSandbox();

        state.Load("return marker == nil and getfenv == nil and setfenv == nil").Execute<bool>().ShouldBeTrue();
    }

    [Fact]
    public void Script_ShouldSeeLiveHostObjectsAndReplacedBuiltinsAsBefore()
    {
        using var state = new LuauState();
        using LuauTable host = state.Load("return { nested = { value = 1 } }").Execute<LuauTable>();
        state.Globals.Set("host", host);
        var live = new Live { Value = 1 };
        using LuauUserdata liveUserdata = state.GetOrCreateUserdata(live);
        state.Globals.Set("live", liveUserdata);
        using LuauFunction floor = state.CreateFunctionManual(static _ => LuauReturn.Ok(42));
        using (LuauTable math = state.Globals.GetLuauTable("math"))
            math.Set("floor", floor);
        state.Load("rawset = nil").Execute();
        state.EnableSandbox();

        // Luau can resolve 'a.b.c' and call built-in functions when a script is loaded instead of when it runs.
        // Nothing here may be read early or bypassed.
        using LuauFunction read = state.Load("return live.value").ToFunction();
        live.Reads.ShouldBe(0);
        read.Invoke<int>().ShouldBe(1);
        live.Value = 7;
        read.Invoke<int>().ShouldBe(7);
        state.Load("host.nested.value = 2; return host.nested.value").Execute<int>().ShouldBe(2);
        state.Load("return math.floor(1.5)").Execute<int>().ShouldBe(42);
        Should.Throw<LuaException>(() => state.Load("rawset({}, 'a', 1)").Execute());
    }

    private sealed class Live : ILuauUserdata<Live>
    {
        public int Value { get; set; }

        public int Reads { get; private set; }

        public static void Register(LuauUserdataRegistry<Live> registry) =>
            registry.AddGetter(
                "value",
                static (self, _) =>
                {
                    self.Reads++;
                    return LuauReturnSingle.Ok(self.Value);
                }
            );
    }
}
