using Shouldly;

namespace Darp.Luau.Tests.Require;

public sealed class HostModuleTests
{
    [Fact]
    public void ScriptModule_ShouldRequireRegisteredHostModule()
    {
        using var state = new LuauState();
        state.RegisterModule("myHost", static (_, in module) => module.Set("answer", 42));

        const string strSource = """return require("myHost").answer""";
        int result = state.Load(strSource).Execute<int>();

        result.ShouldBe(42);
    }

    [Fact]
    public void ScriptFile_ShouldRequireRegisteredHostModule()
    {
        var fs = new FakeFileSystem([("./main.luau", """return require("myHost").answer""")]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.RegisterModule("myHost", static (_, in module) => module.Set("answer", 42));
        state.EnableScriptModules();

        int result = state.LoadFile("./main.luau").Execute<int>();

        result.ShouldBe(42);
    }

    [Fact]
    public void ScriptModule_RequiredHostModuleNotPresent_ShouldThrow()
    {
        using var state = new LuauState();
        state.RegisterModule("myHost", static (_, in module) => module.Set("answer", 42));

        const string strSource = """return require("myOtherHost")""";
        LuaException exception = Should.Throw<LuaException>(() => state.Load(strSource).Execute<int>());

        exception.Message.ShouldContain("module 'myOtherHost' is not registered");
    }

    [Fact]
    public void Require_NonStringArgument_ShouldThrowTypeError()
    {
        using var state = new LuauState();
        state.RegisterModule("dummy", static (_, in _) => { });

        LuaException exception = Should.Throw<LuaException>(() => state.Load("return require(123)").Execute());

        exception.Message.ShouldContain("bad argument #1 to 'require'");
        exception.Message.ShouldContain("string expected");
    }

    [Fact]
    public void Require_ScriptModulePathBeforeEnable_ShouldThrowDisabledError()
    {
        var fs = new FakeFileSystem([("./dependency.luau", "return { answer = 42 }")]);

        using var state = new LuauState(LuauLibraries.All, fs);
        state.RegisterModule("dummy", static (_, in _) => { });

        LuaException exception = Should.Throw<LuaException>(() =>
            state.Load("""return require("./dependency")""").Execute<LuauTable>()
        );

        exception.Message.ShouldContain("script module require is not enabled");
    }

    [Fact]
    public void HostModule_OnLoadException_ShouldSurfaceRequireErrorAndNotCacheFailure()
    {
        using var state = new LuauState();
        bool shouldFail = true;
        int loadCount = 0;
        state.RegisterModule("fragile", (_, in module) =>
        {
            loadCount++;
            if (shouldFail)
                throw new InvalidOperationException("boom from OnLoad");

            module.Set("answer", 42);
        });

        LuaException exception = Should.Throw<LuaException>(() =>
            state.Load("""return require("fragile")""").Execute<LuauTable>()
        );
        exception.Message.ShouldContain("failed to load module 'fragile'");
        exception.Message.ShouldContain("boom from OnLoad");

        shouldFail = false;
        int result = state.Load("""return require("fragile").answer""").Execute<int>();

        result.ShouldBe(42);
        loadCount.ShouldBe(2);
    }

    [Fact]
    public void HostModule_CachedValueDisposed_ShouldUseRequireCallbackExceptionBoundary()
    {
        using var state = new LuauState();
        LuauTable cachedModule = default;
        state.RegisterModule("fragile", (_, in module) =>
        {
            cachedModule = module;
            module.Set("answer", 42);
        });

        state.Load("""return require("fragile").answer""").Execute<int>().ShouldBe(42);

        cachedModule.Dispose();

        LuaException exception = Should.Throw<LuaException>(() =>
            state.Load("""return require("fragile").answer""").Execute<int>()
        );
        exception.Message.ShouldContain("require callback failed");
        exception.Message.ShouldContain("ObjectDisposedException");

        state.RegisterModule("healthy", static (_, in module) => module.Set("answer", 42));
        state.Load("""return require("healthy").answer""").Execute<int>().ShouldBe(42);
    }

    [Fact]
    public void HostModule_RecursiveRequire_ShouldSurfaceAlreadyLoadingError()
    {
        using var state = new LuauState();
        state.RegisterModule("recursive", (lua, in _) =>
            lua.Load("""return require("recursive")""").Execute<LuauTable>()
        );

        LuaException exception = Should.Throw<LuaException>(() =>
            state.Load("""return require("recursive")""").Execute<LuauTable>()
        );

        exception.Message.ShouldContain("module 'recursive' is already loading");
    }
}
